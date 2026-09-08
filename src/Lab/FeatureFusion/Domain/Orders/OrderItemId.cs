using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Orders;

/// <summary>Strongly-typed order-line identity (orders bounded context).</summary>
public sealed record OrderItemId : EntityId<int>
{
	/// <summary>Creates an order-line id. Zero and negatives are allowed (EF temporary keys before insert).</summary>
	public OrderItemId(int value) : base(value)
	{
	}

	/// <summary>Factory used by EF converters and domain code.</summary>
	public static OrderItemId From(int value) => new(value);

	/// <summary>Implicit from the persistence primitive.</summary>
	public static implicit operator OrderItemId(int value) => new(value);
}
