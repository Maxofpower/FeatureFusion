using BuildingBlocks.Mediator;
using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.CursorPagination;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Customers;

/// <summary>
/// Customer list via <see cref="EntityFrameworkCursorExtensions.ToCursorPageAsync"/> —
/// Demo Commerce showcase of BuildingBlocks.Pagination (distinct from storefront OFFSET catalog).
/// </summary>
public sealed class ListCustomersQueryHandler
	: IQueryHandler<ListCustomersQuery, Result<PagedResult<CustomerListItemDto>>>
{
	private readonly CatalogDbContext _db;

	public ListCustomersQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<PagedResult<CustomerListItemDto>>> Handle(
		ListCustomersQuery request,
		CancellationToken cancellationToken)
	{
		var limit = request.Limit is < 1 or > 50 ? 20 : request.Limit;
		var firstPage = string.IsNullOrWhiteSpace(request.Cursor);

		try
		{
			var page = await _db.Customers
				.AsNoTracking()
				.TagWith("customers.list")
				.ToCursorPageAsync(
					new CursorRequest(request.Cursor, limit),
					CustomerSortKeys.CreatedAtDesc,
					c => new CustomerListItemDto(
						(int)c.Id,
						c.Email.Value,
						c.DisplayName,
						c.CreatedAt),
					new PaginationOptions { IncludeTotalCount = firstPage },
					cancellationToken)
				.ConfigureAwait(false);

			return Result<PagedResult<CustomerListItemDto>>.Success(page.ToPagedResult());
		}
		catch (PaginationException ex)
		{
			return Result<PagedResult<CustomerListItemDto>>.Failure(
				ex.Message,
				StatusCodes.Status400BadRequest);
		}
	}
}
