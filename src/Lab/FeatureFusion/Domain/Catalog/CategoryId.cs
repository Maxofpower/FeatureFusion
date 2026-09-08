using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Strongly-typed category identity (catalog bounded context).</summary>
public sealed record CategoryId : EntityId<int>
{
	/// <summary>Creates a category id. Zero and negatives are allowed (EF temporary keys before insert).</summary>
	public CategoryId(int value) : base(value)
	{
	}

	/// <summary>Factory used by EF converters and domain code.</summary>
	public static CategoryId From(int value) => new(value);

	/// <summary>Implicit from the persistence primitive.</summary>
	public static implicit operator CategoryId(int value) => new(value);
}
