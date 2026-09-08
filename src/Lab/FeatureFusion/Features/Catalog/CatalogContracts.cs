using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;

namespace FeatureFusion.Features.Catalog;

/// <summary>Listing card for the product grid.</summary>
public sealed record CatalogProductListItemDto(
	int Id,
	string Name,
	string Slug,
	string Sku,
	decimal Price,
	int StockQuantity,
	bool InStock,
	string BrandName,
	string BrandSlug,
	string CategoryName,
	string CategorySlug,
	string? PrimaryImageUrl,
	string? ShortDescription);

/// <summary>Paged listing response.</summary>
public sealed record CatalogProductListDto(
	IReadOnlyList<CatalogProductListItemDto> Items,
	int Page,
	int PageSize,
	int TotalCount);

/// <summary>Gallery image on the detail page.</summary>
public sealed record CatalogProductImageDto(string Url, string AltText, bool IsPrimary, int DisplayOrder);

/// <summary>Specification row on the detail page.</summary>
public sealed record CatalogProductSpecDto(string Name, string Value, int DisplayOrder);

/// <summary>Related product chip on the detail page.</summary>
public sealed record CatalogRelatedProductDto(int Id, string Name, string Slug, decimal Price, string? PrimaryImageUrl);

/// <summary>Full product detail.</summary>
public sealed record CatalogProductDetailDto(
	int Id,
	string Name,
	string Slug,
	string Sku,
	decimal Price,
	int StockQuantity,
	bool InStock,
	string? ShortDescription,
	string? FullDescription,
	string BrandName,
	string BrandSlug,
	string CategoryName,
	string CategorySlug,
	IReadOnlyList<CatalogProductImageDto> Images,
	IReadOnlyList<CatalogProductSpecDto> Specifications,
	IReadOnlyList<CatalogRelatedProductDto> Related);

/// <summary>Brand chip for listing filters.</summary>
public sealed record CatalogBrandDto(int Id, string Name, string Slug, string? LogoUrl, int ProductCount);

/// <summary>Category chip for listing filters.</summary>
public sealed record CatalogCategoryDto(int Id, string Name, string Slug, int ProductCount);

/// <summary>Listing query with optional brand/category slugs.</summary>
[McpTool("catalog.products.list", Description = "List Demo Commerce storefront products (OFFSET, brand/category filters).")]
public sealed record ListCatalogProductsQuery : IQuery<Result<CatalogProductListDto>>
{
	/// <summary>Brand slug filter.</summary>
	public string? Brand { get; init; }

	/// <summary>Category slug filter.</summary>
	public string? Category { get; init; }

	/// <summary>1-based page.</summary>
	public int Page { get; init; } = 1;

	/// <summary>Page size (1–48).</summary>
	public int PageSize { get; init; } = 24;
}

/// <summary>Detail query by public slug (HTTP + MCP share this Mediator message).</summary>
[McpTool("catalog.product.get", Description = "Get a Demo Commerce storefront product by slug (gallery, specs, related).")]
public sealed record GetCatalogProductBySlugQuery(string Slug) : IQuery<Result<CatalogProductDetailDto>>;

/// <summary>Related products in the same category (deterministic Name, Id order).</summary>
public sealed record ListRelatedCatalogProductsQuery(string Slug, int Limit = 8)
	: IQuery<Result<IReadOnlyList<CatalogRelatedProductDto>>>;

/// <summary>All brands that currently have published products.</summary>
public sealed record ListCatalogBrandsQuery : IQuery<Result<IReadOnlyList<CatalogBrandDto>>>;

/// <summary>All categories that currently have published products.</summary>
public sealed record ListCatalogCategoriesQuery : IQuery<Result<IReadOnlyList<CatalogCategoryDto>>>;
