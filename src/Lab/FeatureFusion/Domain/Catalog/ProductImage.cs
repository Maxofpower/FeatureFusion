using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Gallery image owned by a product (detail page carousel; listing uses the primary).</summary>
public class ProductImage : Entity<ProductImageId>
{
	/// <summary>Owning product.</summary>
	public ProductId ProductId { get; private set; } = null!;

	/// <summary>Public media path or URL.</summary>
	public string Url { get; private set; } = "";

	/// <summary>Accessible alternative text.</summary>
	public string AltText { get; private set; } = "";

	/// <summary>Sort order within the gallery (ascending).</summary>
	public int DisplayOrder { get; private set; }

	/// <summary>True when this image is the listing thumbnail.</summary>
	public bool IsPrimary { get; private set; }

	private ProductImage()
	{
	}

	internal static ProductImage Create(
		ProductId productId,
		string url,
		string altText,
		int displayOrder,
		bool isPrimary,
		ProductImageId? id = null)
	{
		if (string.IsNullOrWhiteSpace(url))
			throw new DomainException("Image URL is required.");
		if (displayOrder < 0)
			throw new DomainException("Image display order cannot be negative.");

		var image = new ProductImage
		{
			ProductId = productId,
			Url = url.Trim(),
			AltText = string.IsNullOrWhiteSpace(altText) ? "" : altText.Trim(),
			DisplayOrder = displayOrder,
			IsPrimary = isPrimary
		};
		if (id is not null)
			image.Id = id;
		return image;
	}

	internal void ClearPrimary() => IsPrimary = false;
}
