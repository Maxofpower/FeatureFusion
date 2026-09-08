using BuildingBlocks.Domain;
using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Customers;

namespace FeatureFusion.Domain.Carts;

/// <summary>One cart per customer. Does not own product prices.</summary>
public class Cart : AggregateRoot<CartId>
{
	private readonly List<CartItem> _items = [];

	public CustomerId CustomerId { get; private set; } = null!;
	public DateTime UpdatedAtUtc { get; private set; }
	public IReadOnlyCollection<CartItem> Items => _items;

	private Cart()
	{
	}

	public static Cart Create(CustomerId customerId, DateTime updatedAtUtc, CartId? id = null)
	{
		ArgumentNullException.ThrowIfNull(customerId);
		if (updatedAtUtc.Kind != DateTimeKind.Utc)
			throw new DomainException("UpdatedAt must be UTC.");

		var cart = new Cart
		{
			CustomerId = customerId,
			UpdatedAtUtc = updatedAtUtc
		};
		if (id is not null)
			cart.Id = id;
		return cart;
	}

	public void AddOrIncrement(ProductId productId, int quantity, DateTime utcNow)
	{
		ArgumentNullException.ThrowIfNull(productId);
		if (quantity <= 0)
			throw new DomainException("Quantity must be positive.");
		Touch(utcNow);

		var existing = _items.FirstOrDefault(i => i.ProductId == productId);
		if (existing is null)
			_items.Add(CartItem.Create(Id ?? CartId.From(0), productId, quantity));
		else
			existing.AddQuantity(quantity);
	}

	public void SetQuantity(ProductId productId, int quantity, DateTime utcNow)
	{
		ArgumentNullException.ThrowIfNull(productId);
		Touch(utcNow);
		var existing = _items.FirstOrDefault(i => i.ProductId == productId)
			?? throw new DomainException("Cart item not found.");
		if (quantity <= 0)
		{
			_items.Remove(existing);
			return;
		}

		existing.SetQuantity(quantity);
	}

	public void Remove(ProductId productId, DateTime utcNow)
	{
		ArgumentNullException.ThrowIfNull(productId);
		Touch(utcNow);
		var existing = _items.FirstOrDefault(i => i.ProductId == productId);
		if (existing is not null)
			_items.Remove(existing);
	}

	public void Clear(DateTime utcNow)
	{
		Touch(utcNow);
		_items.Clear();
	}

	private void Touch(DateTime utcNow)
	{
		if (utcNow.Kind != DateTimeKind.Utc)
			throw new DomainException("UpdatedAt must be UTC.");
		UpdatedAtUtc = utcNow;
	}
}
