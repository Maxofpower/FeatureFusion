using BuildingBlocks.Domain;
using FeatureFusion.Domain.Catalog;

namespace FeatureFusion.Domain.Carts;

/// <summary>Cart line — quantity only; catalog price is never stored here.</summary>
public class CartItem : Entity<CartItemId>
{
	public CartId CartId { get; private set; } = null!;
	public Cart? Cart { get; private set; }
	public ProductId ProductId { get; private set; } = null!;
	public int Quantity { get; private set; }

	private CartItem()
	{
	}

	internal static CartItem Create(CartId cartId, ProductId productId, int quantity, CartItemId? id = null)
	{
		if (quantity <= 0)
			throw new DomainException("Cart item quantity must be positive.");
		var item = new CartItem
		{
			CartId = cartId,
			ProductId = productId,
			Quantity = quantity
		};
		if (id is not null)
			item.Id = id;
		return item;
	}

	internal void SetQuantity(int quantity)
	{
		if (quantity <= 0)
			throw new DomainException("Cart item quantity must be positive.");
		Quantity = quantity;
	}

	internal void AddQuantity(int delta)
	{
		if (delta <= 0)
			throw new DomainException("Quantity delta must be positive.");
		var next = Quantity + delta;
		if (next <= 0)
			throw new DomainException("Cart item quantity overflow.");
		Quantity = next;
	}
}
