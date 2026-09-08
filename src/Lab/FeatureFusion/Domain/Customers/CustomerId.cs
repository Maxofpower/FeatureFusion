using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Customers;

/// <summary>Strongly-typed customer identity (customer bounded context).</summary>
public sealed record CustomerId : AggregateId<int>
{
	/// <summary>Creates a customer id. Zero and negatives are allowed (EF temporary keys before insert).</summary>
	public CustomerId(int value) : base(value)
	{
	}

	/// <summary>Factory used by EF converters and domain code.</summary>
	public static CustomerId From(int value) => new(value);

	/// <summary>Implicit from the persistence primitive.</summary>
	public static implicit operator CustomerId(int value) => new(value);

	/// <summary>To the persistence primitive. Required so keyset OrderBy can convert in expression trees.</summary>
	public static explicit operator int(CustomerId id) => id.Value;
}
