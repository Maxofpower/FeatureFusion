using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Carts;

public sealed record CartItemId : EntityId<int>
{
	public CartItemId(int value) : base(value)
	{
	}

	public static CartItemId From(int value) => new(value);
	public static implicit operator CartItemId(int value) => new(value);
	public static explicit operator int(CartItemId id) => id.Value;
}
