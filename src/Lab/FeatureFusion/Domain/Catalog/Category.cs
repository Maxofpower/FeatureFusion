using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Flat catalog category used for listing filters and detail breadcrumbs.</summary>
public class Category : Entity<CategoryId>
{
	/// <summary>Display name.</summary>
	public string Name { get; private set; } = "";

	/// <summary>Stable public slug.</summary>
	public Slug Slug { get; private set; } = null!;

	private Category()
	{
	}

	/// <summary>Creates a category.</summary>
	public static Category Create(string name, Slug? slug = null, CategoryId? id = null)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new DomainException("Category name is required.");

		var category = new Category
		{
			Name = name.Trim(),
			Slug = slug ?? Slug.FromName(name)
		};
		if (id is not null)
			category.Id = id;
		return category;
	}
}
