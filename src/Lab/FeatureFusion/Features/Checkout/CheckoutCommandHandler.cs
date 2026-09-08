using System.Diagnostics;
using BuildingBlocks.Mediator;
using FeatureFusion.Domain.Payments;
using FeatureFusion.Features.Orders.Commands;
using FeatureFusion.Features.Payments;
using FeatureFusion.Features.Shipping;
using FeatureFusion.Features.Tax;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Checkout;

/// <summary>
/// Orchestrates cart → totals → payment → <see cref="CreateOrderCommand"/>.
/// Does not duplicate order aggregate write logic.
/// </summary>
public sealed class CheckoutCommandHandler : ICommandHandler<CheckoutCommand, Result<CheckoutResponse>>
{
	public static readonly ActivitySource ActivitySource = new("FeatureFusion.Checkout");

	private readonly CatalogDbContext _db;
	private readonly ISender _sender;
	private readonly ITaxCalculator _tax;
	private readonly IShippingPolicy _shipping;
	private readonly IPaymentProcessor _payments;
	private readonly TimeProvider _time;

	public CheckoutCommandHandler(
		CatalogDbContext db,
		ISender sender,
		ITaxCalculator tax,
		IShippingPolicy shipping,
		IPaymentProcessor payments,
		TimeProvider? time = null)
	{
		_db = db;
		_sender = sender;
		_tax = tax;
		_shipping = shipping;
		_payments = payments;
		_time = time ?? TimeProvider.System;
	}

	public async Task<Result<CheckoutResponse>> Handle(
		CheckoutCommand request,
		CancellationToken cancellationToken)
	{
		using var activity = ActivitySource.StartActivity("checkout");
		activity?.SetTag("commerce.customer_id", request.CustomerId);

		var cart = await _db.Carts
			.Include(c => c.Items)
			.FirstOrDefaultAsync(c => (int)c.CustomerId == request.CustomerId, cancellationToken)
			.ConfigureAwait(false);

		if (cart is null || cart.Items.Count == 0)
			return Result<CheckoutResponse>.Failure("Cart is empty.", StatusCodes.Status400BadRequest);

		var productIds = cart.Items.Select(i => (int)i.ProductId).Distinct().ToList();
		var products = await _db.Product
			.AsNoTracking()
			.Where(p => productIds.Contains((int)p.Id))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		if (products.Count != productIds.Count)
		{
			var found = products.Select(p => (int)p.Id).ToHashSet();
			var missing = productIds.First(id => !found.Contains(id));
			return Result<CheckoutResponse>.Failure(
				$"Product '{missing}' not found.",
				StatusCodes.Status404NotFound);
		}

		var byId = products.ToDictionary(p => (int)p.Id);
		foreach (var item in cart.Items)
		{
			var productId = (int)item.ProductId;
			var product = byId[productId];
			if (!product.CanFulfill(item.Quantity))
			{
				if (!product.Published || product.Deleted)
					return Result<CheckoutResponse>.Failure(
						$"Product '{productId}' is not available for sale.",
						StatusCodes.Status409Conflict);

				return Result<CheckoutResponse>.Failure(
					$"Product '{productId}' does not have enough stock for quantity {item.Quantity}.",
					StatusCodes.Status409Conflict);
			}
		}

		var subtotal = decimal.Round(
			cart.Items.Sum(i => byId[(int)i.ProductId].Price * i.Quantity),
			2,
			MidpointRounding.AwayFromZero);

		var tax = _tax.Calculate(subtotal);

		ShippingQuote shipping;
		try
		{
			shipping = _shipping.Quote(
				request.RecipientName,
				request.Line1,
				request.City,
				request.PostalCode,
				request.Country);
		}
		catch (BuildingBlocks.Domain.DomainException ex)
		{
			return Result<CheckoutResponse>.Failure(ex.Message, StatusCodes.Status400BadRequest);
		}

		var grand = decimal.Round(
			subtotal + tax.TaxAmount + shipping.Fee,
			2,
			MidpointRounding.AwayFromZero);

		activity?.SetTag("commerce.subtotal", (double)subtotal);
		activity?.SetTag("commerce.grand_total", (double)grand);

		var charge = await _payments.ChargeAsync(
			new PaymentChargeRequest(grand, "EUR", request.CustomerId),
			cancellationToken).ConfigureAwait(false);

		activity?.SetTag("commerce.payment_decision", charge.Decision.ToString());

		var now = UtcNow();

		if (charge.Decision == PaymentDecision.Declined)
		{
			_db.PaymentRecords.Add(PaymentRecord.Create(
				Guid.NewGuid(),
				PaymentOutcome.Declined,
				grand,
				now));
			await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return Result<CheckoutResponse>.Failure(
				charge.Reason,
				StatusCodes.Status402PaymentRequired);
		}

		var createResult = await _sender.Send(
			new CreateOrderCommand
			{
				CustomerId = request.CustomerId,
				Items = cart.Items
					.Select(i => new CreateOrderLineDto
					{
						ProductId = (int)i.ProductId,
						Quantity = i.Quantity
					})
					.ToList(),
				TaxAmount = tax.TaxAmount,
				ShippingAmount = shipping.Fee,
				Shipping = shipping.Details
			},
			cancellationToken).ConfigureAwait(false);

		if (!createResult.IsSuccess)
			return Result<CheckoutResponse>.Failure(createResult.Error, createResult.StatusCode);

		var order = createResult.Value;
		_db.PaymentRecords.Add(PaymentRecord.Create(
			order.OrderId,
			PaymentOutcome.Approved,
			grand,
			now,
			order.DomainOrderId));

		cart.Clear(now);
		await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		activity?.SetTag("commerce.domain_order_id", order.DomainOrderId);

		return Result<CheckoutResponse>.Success(new CheckoutResponse(
			order.OrderId,
			order.DomainOrderId,
			order.OrderNumber,
			order.Status,
			order.Subtotal,
			order.TaxAmount,
			order.ShippingAmount,
			order.TotalAmount,
			"EUR",
			charge.Decision.ToString(),
			order.OrderDate));
	}

	private DateTime UtcNow()
	{
		var now = _time.GetUtcNow().UtcDateTime;
		return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
	}
}
