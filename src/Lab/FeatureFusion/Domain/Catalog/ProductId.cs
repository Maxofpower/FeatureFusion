using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Strongly-typed catalog product identity (catalog bounded context).</summary>
public sealed record ProductId : AggregateId<int>
{
	/// <summary>Creates a product id. Zero and negatives are allowed (EF temporary keys before insert).</summary>
	public ProductId(int value) : base(value)
	{
	}

	/// <summary>Factory used by EF converters and domain code.</summary>
	public static ProductId From(int value) => new(value);

	/// <summary>Implicit from the persistence primitive.</summary>
	public static implicit operator ProductId(int value) => new(value);

	/// <summary>To the persistence primitive. Required so keyset OrderBy can convert in expression trees.</summary>
	public static explicit operator int(ProductId id) => id.Value;
}
