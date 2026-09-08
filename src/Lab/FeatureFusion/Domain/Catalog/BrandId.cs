using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Strongly-typed brand identity (catalog bounded context).</summary>
public sealed record BrandId : EntityId<int>
{
	/// <summary>Creates a brand id. Zero and negatives are allowed (EF temporary keys before insert).</summary>
	public BrandId(int value) : base(value)
	{
	}

	/// <summary>Factory used by EF converters and domain code.</summary>
	public static BrandId From(int value) => new(value);

	/// <summary>Implicit from the persistence primitive.</summary>
	public static implicit operator BrandId(int value) => new(value);
}
