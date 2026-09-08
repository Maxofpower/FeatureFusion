using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Catalog;

public sealed class ListCatalogCategoriesQueryHandler
	: IQueryHandler<ListCatalogCategoriesQuery, Result<IReadOnlyList<CatalogCategoryDto>>>
{
	private readonly CatalogDbContext _db;

	public ListCatalogCategoriesQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<IReadOnlyList<CatalogCategoryDto>>> Handle(
		ListCatalogCategoriesQuery request,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<CatalogCategoryDto> rows = await CatalogProjections.Categories(_db)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return Result<IReadOnlyList<CatalogCategoryDto>>.Success(rows);
	}
}
