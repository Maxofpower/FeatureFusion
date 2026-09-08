using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Catalog;

public sealed class ListCatalogProductsQueryHandler
	: IQueryHandler<ListCatalogProductsQuery, Result<CatalogProductListDto>>
{
	private readonly CatalogDbContext _db;

	public ListCatalogProductsQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<CatalogProductListDto>> Handle(
		ListCatalogProductsQuery request,
		CancellationToken cancellationToken)
	{
		var page = request.Page < 1 ? 1 : request.Page;
		var pageSize = request.PageSize is < 1 or > 48 ? 24 : request.PageSize;

		var query = _db.Product.AsNoTracking()
			.Where(p => p.Published && !p.Deleted && p.VisibleIndividually);

		if (!string.IsNullOrWhiteSpace(request.Brand))
		{
			if (!CatalogSlug.TryCreate(request.Brand, out var brandSlug))
				return EmptyList(page, pageSize);

			var brandId = await _db.Brands.AsNoTracking()
				.Where(b => b.Slug == brandSlug)
				.Select(b => b.Id)
				.FirstOrDefaultAsync(cancellationToken)
				.ConfigureAwait(false);
			if (brandId is null)
				return EmptyList(page, pageSize);

			query = query.Where(p => p.BrandId == brandId);
		}

		if (!string.IsNullOrWhiteSpace(request.Category))
		{
			if (!CatalogSlug.TryCreate(request.Category, out var categorySlug))
				return EmptyList(page, pageSize);

			var categoryId = await _db.Categories.AsNoTracking()
				.Where(c => c.Slug == categorySlug)
				.Select(c => c.Id)
				.FirstOrDefaultAsync(cancellationToken)
				.ConfigureAwait(false);
			if (categoryId is null)
				return EmptyList(page, pageSize);

			query = query.Where(p => p.CategoryId == categoryId);
		}

		var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
		var items = await query
			.OrderBy(p => p.Name)
			.ThenBy(p => (int)p.Id)
			.Skip((page - 1) * pageSize)
			.Take(pageSize)
			.Select(CatalogProjections.ListItem)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return Result<CatalogProductListDto>.Success(new CatalogProductListDto(items, page, pageSize, total));
	}

	private static Result<CatalogProductListDto> EmptyList(int page, int pageSize) =>
		Result<CatalogProductListDto>.Success(new CatalogProductListDto([], page, pageSize, 0));
}
