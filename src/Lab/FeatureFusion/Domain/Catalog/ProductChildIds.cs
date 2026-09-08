using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Strongly-typed product image identity.</summary>
public sealed record ProductImageId : EntityId<int>
{
	/// <summary>Creates an image id. Zero and negatives are allowed (EF temporary keys before insert).</summary>
	public ProductImageId(int value) : base(value)
	{
	}

	/// <summary>Factory used by converters and domain code.</summary>
	public static ProductImageId From(int value) => new(value);

	/// <summary>Implicit from the persistence primitive.</summary>
	public static implicit operator ProductImageId(int value) => new(value);
}

/// <summary>Strongly-typed product specification identity.</summary>
public sealed record ProductSpecificationId : EntityId<int>
{
	/// <summary>Creates a specification id. Zero and negatives are allowed (EF temporary keys before insert).</summary>
	public ProductSpecificationId(int value) : base(value)
	{
	}

	/// <summary>Factory used by converters and domain code.</summary>
	public static ProductSpecificationId From(int value) => new(value);

	/// <summary>Implicit from the persistence primitive.</summary>
	public static implicit operator ProductSpecificationId(int value) => new(value);
}
