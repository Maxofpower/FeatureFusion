using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Name/value specification row shown on the product detail page.</summary>
public class ProductSpecification : Entity<ProductSpecificationId>
{
	/// <summary>Owning product.</summary>
	public ProductId ProductId { get; private set; } = null!;

	/// <summary>Specification label (for example Display or Battery).</summary>
	public string Name { get; private set; } = "";

	/// <summary>Specification value.</summary>
	public string Value { get; private set; } = "";

	/// <summary>Sort order on the detail page (ascending).</summary>
	public int DisplayOrder { get; private set; }

	private ProductSpecification()
	{
	}

	internal static ProductSpecification Create(
		ProductId productId,
		string name,
		string value,
		int displayOrder,
		ProductSpecificationId? id = null)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new DomainException("Specification name is required.");
		if (string.IsNullOrWhiteSpace(value))
			throw new DomainException("Specification value is required.");
		if (displayOrder < 0)
			throw new DomainException("Specification display order cannot be negative.");

		var spec = new ProductSpecification
		{
			ProductId = productId,
			Name = name.Trim(),
			Value = value.Trim(),
			DisplayOrder = displayOrder
		};
		if (id is not null)
			spec.Id = id;
		return spec;
	}
}
