using System.Linq.Expressions;
using FeatureFusion.Domain.Catalog;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Catalog;

/// <summary>
/// EF-translatable catalog projections. HTTP DTOs stay primitives; aggregates stay in the domain.
/// </summary>
public static class CatalogProjections
{
	public static readonly Expression<Func<Product, CatalogProductListItemDto>> ListItem =
		p => new CatalogProductListItemDto(
			(int)p.Id,
			p.Name,
			p.Slug.Value,
			p.Sku.Value,
			p.Price,
			p.StockQuantity,
			p.StockQuantity > 0,
			p.Brand!.Name,
			p.Brand.Slug.Value,
			p.Category!.Name,
			p.Category.Slug.Value,
			p.Images
				.OrderByDescending(i => i.IsPrimary)
				.ThenBy(i => i.DisplayOrder)
				.Select(i => i.Url)
				.FirstOrDefault(),
			p.ShortDescription);

	internal static readonly Expression<Func<Product, CatalogProductDetailRow>> Detail =
		p => new CatalogProductDetailRow(
			(int)p.Id,
			p.Name,
			p.Slug.Value,
			p.Sku.Value,
			p.Price,
			p.StockQuantity,
			p.StockQuantity > 0,
			p.ShortDescription,
			p.FullDescription,
			p.Brand!.Name,
			p.Brand.Slug.Value,
			p.Category!.Name,
			p.Category.Slug.Value,
			p.Images
				.OrderBy(i => i.DisplayOrder)
				.Select(i => new CatalogProductImageDto(i.Url, i.AltText, i.IsPrimary, i.DisplayOrder))
				.ToList(),
			p.Specifications
				.OrderBy(s => s.DisplayOrder)
				.Select(s => new CatalogProductSpecDto(s.Name, s.Value, s.DisplayOrder))
				.ToList(),
			p.CategoryId);

	public static readonly Expression<Func<Product, CatalogRelatedProductDto>> RelatedItem =
		p => new CatalogRelatedProductDto(
			(int)p.Id,
			p.Name,
			p.Slug.Value,
			p.Price,
			p.Images
				.OrderByDescending(i => i.IsPrimary)
				.ThenBy(i => i.DisplayOrder)
				.Select(i => i.Url)
				.FirstOrDefault());

	public static IQueryable<CatalogBrandDto> Brands(CatalogDbContext db) =>
		db.Brands.AsNoTracking()
			.OrderBy(b => b.Name)
			.Select(b => new CatalogBrandDto(
				(int)b.Id,
				b.Name,
				b.Slug.Value,
				b.LogoUrl,
				db.Product.Count(p => p.BrandId == b.Id && p.Published && !p.Deleted)));

	public static IQueryable<CatalogCategoryDto> Categories(CatalogDbContext db) =>
		db.Categories.AsNoTracking()
			.OrderBy(c => c.Name)
			.Select(c => new CatalogCategoryDto(
				(int)c.Id,
				c.Name,
				c.Slug.Value,
				db.Product.Count(p => p.CategoryId == c.Id && p.Published && !p.Deleted)));
}

/// <summary>Detail projection without related products (loaded in a second query).</summary>
internal sealed record CatalogProductDetailRow(
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
	CategoryId CategoryId);
