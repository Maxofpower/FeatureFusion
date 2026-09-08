using BuildingBlocks.Mediator;
using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Customers;
using FeatureFusion.Domain.Orders;
using FeatureFusion.Features.Order.IntegrationEvents;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using OrderEntity = FeatureFusion.Domain.Orders.Order;

namespace FeatureFusion.Features.Orders.Commands;

/// <summary>
/// Persists a real <see cref="OrderEntity"/> with catalog price snapshots and stock decrement.
/// Surfaces (HTTP / MCP / Admission release / Checkout) must only call <see cref="ISender.Send"/>.
/// </summary>
public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, Result<OrderResponse>>
{
	private const int MaxConcurrencyRetries = 5;

	private readonly CatalogDbContext _db;
	private readonly IIntegrationEventService _integrationEvents;
	private readonly TimeProvider _time;

	public CreateOrderCommandHandler(
		CatalogDbContext db,
		IIntegrationEventService integrationEvents,
		TimeProvider? time = null)
	{
		_db = db;
		_integrationEvents = integrationEvents;
		_time = time ?? TimeProvider.System;
	}

	public async Task<Result<OrderResponse>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
	{
		var merged = request.ResolveLines()
			.GroupBy(l => l.ProductId)
			.Select(g => (ProductId: g.Key, Quantity: g.Sum(x => x.Quantity)))
			.ToList();

		var customer = await _db.Customers.AsNoTracking()
			.FirstOrDefaultAsync(c => (int)c.Id == request.CustomerId, cancellationToken)
			.ConfigureAwait(false);
		if (customer is null)
			return Result<OrderResponse>.Failure("Customer not found.", StatusCodes.Status404NotFound);

		var productIds = merged.Select(m => m.ProductId).ToList();

		List<Product>? loadedProducts = null;
		OrderEntity? pendingOrder = null;

		for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
		{
			if (attempt > 0)
			{
				// Discard only this attempt's mutations — never ChangeTracker.Clear()
				// (Checkout / Admission may still track Cart / IntentTicket on the same scoped DbContext).
				await DiscardAttemptAsync(loadedProducts, pendingOrder, cancellationToken).ConfigureAwait(false);
				loadedProducts = null;
				pendingOrder = null;
			}

			var products = await _db.Product
				.Where(p => productIds.Contains((int)p.Id))
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);
			loadedProducts = products;

			if (products.Count != productIds.Count)
			{
				var found = products.Select(p => (int)p.Id).ToHashSet();
				var missing = productIds.First(id => !found.Contains(id));
				return Result<OrderResponse>.Failure($"Product '{missing}' not found.", StatusCodes.Status404NotFound);
			}

			var byId = products.ToDictionary(p => (int)p.Id);

			foreach (var (productId, quantity) in merged)
			{
				var product = byId[productId];
				if (!product.CanFulfill(quantity))
				{
					if (!product.Published || product.Deleted)
						return Result<OrderResponse>.Failure(
							$"Product '{productId}' is not available for sale.",
							StatusCodes.Status409Conflict);

					return Result<OrderResponse>.Failure(
						$"Product '{productId}' does not have enough stock for quantity {quantity}.",
						StatusCodes.Status409Conflict);
				}
			}

			foreach (var (productId, quantity) in merged)
			{
				if (!byId[productId].TryDecrementStock(quantity))
				{
					return Result<OrderResponse>.Failure(
						$"Product '{productId}' does not have enough stock for quantity {quantity}.",
						StatusCodes.Status409Conflict);
				}
			}

			var now = _time.GetUtcNow().UtcDateTime;
			if (now.Kind != DateTimeKind.Utc)
				now = DateTime.SpecifyKind(now, DateTimeKind.Utc);

			var orderLines = merged.Select(m =>
			{
				var product = byId[m.ProductId];
				return (
					ProductId: ProductId.From(m.ProductId),
					m.Quantity,
					UnitPrice: product.Price,
					(OrderItemId?)null);
			}).ToList();

			var orderNumber = OrderNumber.Create($"ORD-{Ulid.NewUlid()}");
			var order = OrderEntity.Create(
				orderNumber,
				CustomerId.From(request.CustomerId),
				OrderStatus.Placed,
				currency: "EUR",
				createdAtUtc: now,
				lines: orderLines,
				taxAmount: request.TaxAmount,
				shippingAmount: request.ShippingAmount,
				shipping: request.Shipping);
			pendingOrder = order;
			_db.Orders.Add(order);

			var correlationId = Guid.NewGuid();
			var evt = new OrderCreatedIntegrationEvent(correlationId, order.Total);

			try
			{
				await _integrationEvents.PublishThroughEventBusAsync(evt).ConfigureAwait(false);
			}
			catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries - 1)
			{
				continue;
			}
			catch (DbUpdateConcurrencyException)
			{
				await DiscardAttemptAsync(loadedProducts, pendingOrder, cancellationToken).ConfigureAwait(false);
				return Result<OrderResponse>.Failure(
					"Stock changed concurrently; please retry.",
					StatusCodes.Status409Conflict);
			}

			var primary = merged[0];
			var primaryProduct = byId[primary.ProductId];

			return Result<OrderResponse>.Success(new OrderResponse
			{
				OrderId = correlationId,
				DomainOrderId = (int)order.Id,
				OrderNumber = order.OrderNumber.Value,
				Status = order.Status.ToString(),
				CustomerName = customer.DisplayName,
				ProductName = primaryProduct.Name,
				Quantity = merged.Sum(m => m.Quantity),
				TotalAmount = order.Total,
				Subtotal = order.Subtotal,
				TaxAmount = order.TaxAmount,
				ShippingAmount = order.ShippingAmount,
				OrderDate = order.CreatedAt,
				Message = "Order created successfully."
			});
		}

		return Result<OrderResponse>.Failure(
			"Stock changed concurrently; please retry.",
			StatusCodes.Status409Conflict);
	}

	/// <summary>
	/// Rolls back in-memory stock mutations (reload from DB) and detaches an uncommitted order
	/// without wiping unrelated tracked entities on the shared scoped <see cref="CatalogDbContext"/>.
	/// </summary>
	private async Task DiscardAttemptAsync(
		List<Product>? products,
		OrderEntity? order,
		CancellationToken cancellationToken)
	{
		if (order is not null)
		{
			foreach (var item in order.Items.ToList())
			{
				var itemEntry = _db.Entry(item);
				if (itemEntry.State != EntityState.Detached)
					itemEntry.State = EntityState.Detached;
			}

			var orderEntry = _db.Entry(order);
			if (orderEntry.State != EntityState.Detached)
				orderEntry.State = EntityState.Detached;
		}

		if (products is null)
			return;

		foreach (var product in products)
		{
			var entry = _db.Entry(product);
			if (entry.State == EntityState.Detached)
				continue;

			// Reload restores StockQuantity + OriginalVersion from the database after a failed SaveChanges.
			await entry.ReloadAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}

/// <summary>Create-order HTTP/MCP response (Exp-compatible Guid OrderId + Demo Commerce fields).</summary>
public sealed class OrderResponse
{
	public Guid OrderId { get; set; }
	public int DomainOrderId { get; set; }
	public string OrderNumber { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string CustomerName { get; set; } = string.Empty;
	public string ProductName { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal TotalAmount { get; set; }
	public decimal Subtotal { get; set; }
	public decimal TaxAmount { get; set; }
	public decimal ShippingAmount { get; set; }
	public DateTime OrderDate { get; set; }
	public string Message { get; set; } = string.Empty;
}
