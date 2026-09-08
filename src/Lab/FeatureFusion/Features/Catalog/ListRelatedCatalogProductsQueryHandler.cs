using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Catalog;

/// <summary>
/// Same-category related products for the storefront. Not a recommendation engine —
/// deterministic <c>Name</c>, then <c>Id</c> order.
/// </summary>
public sealed class ListRelatedCatalogProductsQueryHandler
	: IQueryHandler<ListRelatedCatalogProductsQuery, Result<IReadOnlyList<CatalogRelatedProductDto>>>
{
	private readonly CatalogDbContext _db;

	public ListRelatedCatalogProductsQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<IReadOnlyList<CatalogRelatedProductDto>>> Handle(
		ListRelatedCatalogProductsQuery request,
		CancellationToken cancellationToken)
	{
		if (!CatalogSlug.TryCreate(request.Slug, out var slug))
			return Result<IReadOnlyList<CatalogRelatedProductDto>>.Failure(
				"Slug is required.",
				StatusCodes.Status400BadRequest);

		var limit = request.Limit is < 1 or > 24 ? 8 : request.Limit;

		var source = await _db.Product.AsNoTracking()
			.Where(p => p.Slug == slug && p.Published && !p.Deleted)
			.Select(p => new { Id = (int)p.Id, p.CategoryId })
			.SingleOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

		if (source is null)
			return Result<IReadOnlyList<CatalogRelatedProductDto>>.Failure(
				"Product not found.",
				StatusCodes.Status404NotFound);

		var related = await _db.Product.AsNoTracking()
			.Where(p =>
				p.Published
				&& !p.Deleted
				&& p.VisibleIndividually
				&& p.CategoryId == source.CategoryId
				&& (int)p.Id != source.Id)
			.OrderBy(p => p.Name)
			.ThenBy(p => (int)p.Id)
			.Take(limit)
			.Select(CatalogProjections.RelatedItem)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return Result<IReadOnlyList<CatalogRelatedProductDto>>.Success(related);
	}
}
