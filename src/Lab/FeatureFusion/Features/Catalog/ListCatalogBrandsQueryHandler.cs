using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Catalog;

public sealed class ListCatalogBrandsQueryHandler
	: IQueryHandler<ListCatalogBrandsQuery, Result<IReadOnlyList<CatalogBrandDto>>>
{
	private readonly CatalogDbContext _db;

	public ListCatalogBrandsQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<IReadOnlyList<CatalogBrandDto>>> Handle(
		ListCatalogBrandsQuery request,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<CatalogBrandDto> rows = await CatalogProjections.Brands(_db)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return Result<IReadOnlyList<CatalogBrandDto>>.Success(rows);
	}
}
