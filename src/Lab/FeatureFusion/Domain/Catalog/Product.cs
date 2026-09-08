using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>
/// Catalog product aggregate. Listing pages read name, price, primary image, brand, and category.
/// Detail pages also load gallery images and specifications.
/// </summary>
public class Product : AggregateRoot<ProductId>, IHaveSoftDelete
{
	private readonly List<ProductImage> _images = [];
	private readonly List<ProductSpecification> _specifications = [];

	/// <summary>Display name.</summary>
	public string Name { get; private set; } = "";

	/// <summary>Unique stock-keeping unit.</summary>
	public Sku Sku { get; private set; } = null!;

	/// <summary>Public detail-route slug.</summary>
	public Slug Slug { get; private set; } = null!;

	/// <summary>One-line summary for listing cards.</summary>
	public string? ShortDescription { get; private set; }

	/// <summary>Long copy for the detail page.</summary>
	public string? FullDescription { get; private set; }

	/// <summary>Whether the product appears in listing and detail.</summary>
	public bool Published { get; private set; }

	/// <inheritdoc />
	public bool Deleted { get; private set; }

	/// <summary>Whether the product is sold as a standalone listing.</summary>
	public bool VisibleIndividually { get; private set; } = true;

	/// <summary>Unit price in EUR.</summary>
	public decimal Price { get; private set; }

	/// <summary>Available units. Zero means out of stock.</summary>
	public int StockQuantity { get; private set; }

	/// <summary>Owning brand (required). Identity is the source of truth.</summary>
	public BrandId BrandId { get; private set; } = null!;

	/// <summary>Loaded brand navigation; null when the query did not include it.</summary>
	public Brand? Brand { get; private set; }

	/// <summary>Owning category (required). Identity is the source of truth.</summary>
	public CategoryId CategoryId { get; private set; } = null!;

	/// <summary>Loaded category navigation; null when the query did not include it.</summary>
	public Category? Category { get; private set; }

	/// <summary>UTC creation timestamp (listing sort).</summary>
	public DateTime CreatedAt { get; private set; }

	/// <summary>Gallery owned by this product.</summary>
	public IReadOnlyCollection<ProductImage> Images => _images;

	/// <summary>Specification rows owned by this product.</summary>
	public IReadOnlyCollection<ProductSpecification> Specifications => _specifications;

	private Product()
	{
	}

	/// <summary>Creates a catalog product.</summary>
	public static Product Create(
		string name,
		Sku sku,
		decimal price,
		int stockQuantity,
		BrandId brandId,
		CategoryId categoryId,
		DateTime createdAtUtc,
		Slug? slug = null,
		string? shortDescription = null,
		string? fullDescription = null,
		bool published = true,
		bool visibleIndividually = true,
		ProductId? id = null)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new DomainException("Product name is required.");
		ArgumentNullException.ThrowIfNull(sku);
		ArgumentNullException.ThrowIfNull(brandId);
		ArgumentNullException.ThrowIfNull(categoryId);
		if (price < 0)
			throw new DomainException("Product price cannot be negative.");
		if (stockQuantity < 0)
			throw new DomainException("Stock quantity cannot be negative.");
		if (createdAtUtc.Kind != DateTimeKind.Utc)
			throw new DomainException("CreatedAt must be UTC.");

		var product = new Product
		{
			Name = name.Trim(),
			Sku = sku,
			Slug = slug ?? Slug.FromName(name),
			ShortDescription = string.IsNullOrWhiteSpace(shortDescription) ? null : shortDescription.Trim(),
			FullDescription = fullDescription,
			Price = price,
			StockQuantity = stockQuantity,
			BrandId = brandId,
			CategoryId = categoryId,
			Published = published,
			Deleted = false,
			VisibleIndividually = visibleIndividually,
			CreatedAt = createdAtUtc
		};
		if (id is not null)
			product.Id = id;
		return product;
	}

	/// <summary>Adds a gallery image. The first primary wins; later primaries are demoted.</summary>
	public ProductImage AddImage(string url, string altText, int displayOrder, bool isPrimary, ProductImageId? id = null)
	{
		var productId = Id ?? ProductId.From(0);
		if (isPrimary)
		{
			foreach (var existing in _images)
				existing.ClearPrimary();
		}

		var image = ProductImage.Create(productId, url, altText, displayOrder, isPrimary, id);
		_images.Add(image);
		return image;
	}

	/// <summary>Adds a specification row.</summary>
	public ProductSpecification AddSpecification(string name, string value, int displayOrder, ProductSpecificationId? id = null)
	{
		var productId = Id ?? ProductId.From(0);
		var spec = ProductSpecification.Create(productId, name, value, displayOrder, id);
		_specifications.Add(spec);
		return spec;
	}

	/// <summary>
	/// Whether the product can be sold in the given quantity (published, not deleted, enough stock).
	/// </summary>
	public bool CanFulfill(int quantity) =>
		Published && !Deleted && quantity > 0 && StockQuantity >= quantity;

	/// <summary>
	/// Decrements stock for a fulfilled sale. Never goes negative.
	/// Bumps <see cref="AggregateRoot{TId}.OriginalVersion"/> for optimistic concurrency.
	/// </summary>
	public bool TryDecrementStock(int quantity)
	{
		if (!CanFulfill(quantity))
			return false;
		StockQuantity -= quantity;
		OriginalVersion++;
		return true;
	}

	/// <summary>Updates catalog list price (does not rewrite historical order line snapshots).</summary>
	public void ChangePrice(decimal newPrice)
	{
		if (newPrice < 0)
			throw new DomainException("Product price cannot be negative.");
		Price = newPrice;
		OriginalVersion++;
	}

	/// <summary>In-memory sample used by promotion demos (not persisted).</summary>
	public static Product CreateDemo(string name, bool published, ProductId? id = null)
	{
		return Create(
			name: name,
			sku: Sku.Create($"SKU-DEMO-{(id?.Value ?? 0):D4}"),
			price: 0m,
			stockQuantity: 0,
			brandId: BrandId.From(1),
			categoryId: CategoryId.From(1),
			createdAtUtc: DateTime.UtcNow,
			published: published,
			id: id);
	}
}
