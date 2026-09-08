using BuildingBlocks.Domain;
using FeatureFusion.Domain.Catalog;

namespace FeatureFusion.Domain.Orders;

/// <summary>Order line with unit price captured at placement time.</summary>
public class OrderItem : Entity<OrderItemId>
{
	/// <summary>Owning order.</summary>
	public OrderId OrderId { get; private set; } = null!;

	/// <summary>Loaded order navigation.</summary>
	public Order? Order { get; private set; }

	/// <summary>Catalog product identity.</summary>
	public ProductId ProductId { get; private set; } = null!;

	/// <summary>Loaded product navigation.</summary>
	public Product? Product { get; private set; }

	/// <summary>Quantity ordered.</summary>
	public int Quantity { get; private set; }

	/// <summary>Unit price at order time.</summary>
	public decimal UnitPrice { get; private set; }

	/// <summary>Line total (not persisted).</summary>
	public decimal LineTotal => UnitPrice * Quantity;

	private OrderItem()
	{
	}

	internal static OrderItem Create(
		OrderId orderId,
		ProductId productId,
		int quantity,
		decimal unitPrice,
		OrderItemId? id = null)
	{
		if (quantity <= 0)
			throw new DomainException("Order item quantity must be positive.");
		if (unitPrice < 0)
			throw new DomainException("Unit price cannot be negative.");

		var item = new OrderItem
		{
			OrderId = orderId,
			ProductId = productId,
			Quantity = quantity,
			UnitPrice = unitPrice
		};
		if (id is not null)
			item.Id = id;
		return item;
	}
}
