using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Catalog;

public sealed class GetCatalogProductBySlugQueryHandler
	: IQueryHandler<GetCatalogProductBySlugQuery, Result<CatalogProductDetailDto>>
{
	private readonly CatalogDbContext _db;

	public GetCatalogProductBySlugQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<CatalogProductDetailDto>> Handle(
		GetCatalogProductBySlugQuery request,
		CancellationToken cancellationToken)
	{
		if (!CatalogSlug.TryCreate(request.Slug, out var slug))
			return Result<CatalogProductDetailDto>.Failure("Slug is required.", StatusCodes.Status400BadRequest);

		var row = await _db.Product.AsNoTracking()
			.Where(p => p.Slug == slug && p.Published && !p.Deleted)
			.Select(CatalogProjections.Detail)
			.SingleOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

		if (row is null)
			return Result<CatalogProductDetailDto>.Failure("Product not found.", StatusCodes.Status404NotFound);

		var related = await _db.Product.AsNoTracking()
			.Where(p => p.Published && !p.Deleted && p.CategoryId == row.CategoryId && (int)p.Id != row.Id)
			.OrderBy(p => p.Name)
			.Take(4)
			.Select(CatalogProjections.RelatedItem)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return Result<CatalogProductDetailDto>.Success(new CatalogProductDetailDto(
			row.Id,
			row.Name,
			row.Slug,
			row.Sku,
			row.Price,
			row.StockQuantity,
			row.InStock,
			row.ShortDescription,
			row.FullDescription,
			row.BrandName,
			row.BrandSlug,
			row.CategoryName,
			row.CategorySlug,
			row.Images,
			row.Specifications,
			related));
	}
}
