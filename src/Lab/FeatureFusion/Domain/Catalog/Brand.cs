using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Catalog brand used for listing filters and detail attribution.</summary>
public class Brand : Entity<BrandId>
{
	/// <summary>Display name.</summary>
	public string Name { get; private set; } = "";

	/// <summary>Stable public slug.</summary>
	public Slug Slug { get; private set; } = null!;

	/// <summary>Optional logo path for listing chips and detail headers.</summary>
	public string? LogoUrl { get; private set; }

	private Brand()
	{
	}

	/// <summary>Creates a brand.</summary>
	public static Brand Create(string name, Slug? slug = null, string? logoUrl = null, BrandId? id = null)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new DomainException("Brand name is required.");

		var brand = new Brand
		{
			Name = name.Trim(),
			Slug = slug ?? Slug.FromName(name),
			LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim()
		};
		if (id is not null)
			brand.Id = id;
		return brand;
	}
}
