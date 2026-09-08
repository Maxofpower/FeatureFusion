using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Orders;

/// <summary>Strongly-typed order identity (orders bounded context).</summary>
public sealed record OrderId : AggregateId<int>
{
	/// <summary>Creates an order id. Zero and negatives are allowed (EF temporary keys before insert).</summary>
	public OrderId(int value) : base(value)
	{
	}

	/// <summary>Factory used by EF converters and domain code.</summary>
	public static OrderId From(int value) => new(value);

	/// <summary>Implicit from the persistence primitive.</summary>
	public static implicit operator OrderId(int value) => new(value);

	/// <summary>To the persistence primitive. Required so keyset OrderBy can convert in expression trees.</summary>
	public static explicit operator int(OrderId id) => id.Value;
}
