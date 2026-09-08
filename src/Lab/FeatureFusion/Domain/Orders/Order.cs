using BuildingBlocks.Domain;
using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Customers;

namespace FeatureFusion.Domain.Orders;

/// <summary>Order aggregate with price snapshots and controlled lifecycle transitions.</summary>
public class Order : AggregateRoot<OrderId>
{
	private readonly List<OrderItem> _items = [];

	public OrderNumber OrderNumber { get; private set; } = null!;
	public CustomerId CustomerId { get; private set; } = null!;
	public Customer? Customer { get; private set; }
	public OrderStatus Status { get; private set; }

	/// <summary>Sum of line totals (before tax/shipping).</summary>
	public decimal Subtotal { get; private set; }

	/// <summary>Tax amount calculated at checkout (0 for direct CreateOrder).</summary>
	public decimal TaxAmount { get; private set; }

	/// <summary>Shipping fee calculated at checkout (0 for direct CreateOrder).</summary>
	public decimal ShippingAmount { get; private set; }

	/// <summary>Grand total = Subtotal + TaxAmount + ShippingAmount.</summary>
	public decimal Total { get; private set; }

	public string Currency { get; private set; } = "EUR";
	public DateTime CreatedAt { get; private set; }
	public OrderShipping Shipping { get; private set; } = OrderShipping.None();
	public IReadOnlyCollection<OrderItem> Items => _items;

	private Order()
	{
	}

	/// <summary>
	/// Creates an order with at least one line.
	/// When tax/shipping are omitted, grand total equals line subtotal (CreateOrder / Exp path).
	/// </summary>
	public static Order Create(
		OrderNumber orderNumber,
		CustomerId customerId,
		OrderStatus status,
		string currency,
		DateTime createdAtUtc,
		IEnumerable<(ProductId ProductId, int Quantity, decimal UnitPrice, OrderItemId? Id)> lines,
		OrderId? id = null,
		decimal taxAmount = 0m,
		decimal shippingAmount = 0m,
		OrderShipping? shipping = null)
	{
		ArgumentNullException.ThrowIfNull(orderNumber);
		ArgumentNullException.ThrowIfNull(customerId);
		if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
			throw new DomainException("Currency must be a 3-letter code.");
		if (createdAtUtc.Kind != DateTimeKind.Utc)
			throw new DomainException("CreatedAt must be UTC.");
		if (taxAmount < 0)
			throw new DomainException("Tax amount cannot be negative.");
		if (shippingAmount < 0)
			throw new DomainException("Shipping amount cannot be negative.");

		var lineList = lines?.ToList() ?? throw new DomainException("Order lines are required.");
		if (lineList.Count == 0)
			throw new DomainException("Order must have at least one line.");

		var order = new Order
		{
			OrderNumber = orderNumber,
			CustomerId = customerId,
			Status = status,
			Currency = currency.Trim().ToUpperInvariant(),
			CreatedAt = createdAtUtc,
			TaxAmount = decimal.Round(taxAmount, 2, MidpointRounding.AwayFromZero),
			ShippingAmount = decimal.Round(shippingAmount, 2, MidpointRounding.AwayFromZero),
			Shipping = shipping ?? OrderShipping.None()
		};
		if (id is not null)
			order.Id = id;

		var orderId = id ?? OrderId.From(0);
		foreach (var line in lineList)
			order._items.Add(OrderItem.Create(orderId, line.ProductId, line.Quantity, line.UnitPrice, line.Id));

		order.Subtotal = decimal.Round(order._items.Sum(i => i.LineTotal), 2, MidpointRounding.AwayFromZero);
		order.Total = decimal.Round(
			order.Subtotal + order.TaxAmount + order.ShippingAmount,
			2,
			MidpointRounding.AwayFromZero);
		return order;
	}

	public void MarkPlaced()
	{
		if (Status is not (OrderStatus.Pending or OrderStatus.PaymentFailed))
			throw new DomainException($"Cannot mark Placed from status {Status}.");
		Status = OrderStatus.Placed;
	}

	public void MarkPaymentFailed()
	{
		if (Status != OrderStatus.Pending)
			throw new DomainException($"Cannot mark PaymentFailed from status {Status}.");
		Status = OrderStatus.PaymentFailed;
	}

	public void Cancel()
	{
		if (Status is OrderStatus.Cancelled)
			return;
		if (Status is not (OrderStatus.Pending or OrderStatus.Placed or OrderStatus.PaymentFailed))
			throw new DomainException($"Cannot cancel from status {Status}.");
		Status = OrderStatus.Cancelled;
	}
}
