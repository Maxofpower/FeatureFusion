using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Carts;

public sealed record CartId : AggregateId<int>
{
	public CartId(int value) : base(value)
	{
	}

	public static CartId From(int value) => new(value);
	public static implicit operator CartId(int value) => new(value);
	public static explicit operator int(CartId id) => id.Value;
}
