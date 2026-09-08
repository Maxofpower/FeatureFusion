using BuildingBlocks.Mediator;
using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.CursorPagination;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Orders;

/// <summary>
/// Order list via keyset pagination — Demo Commerce + BuildingBlocks.Pagination.
/// Distinct from lab POST /api/v1/Order/order create and from storefront OFFSET catalog.
/// </summary>
public sealed class ListOrdersQueryHandler
	: IQueryHandler<ListOrdersQuery, Result<PagedResult<OrderListItemDto>>>
{
	private readonly CatalogDbContext _db;

	public ListOrdersQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<PagedResult<OrderListItemDto>>> Handle(
		ListOrdersQuery request,
		CancellationToken cancellationToken)
	{
		var limit = request.Limit is < 1 or > 50 ? 20 : request.Limit;
		var firstPage = string.IsNullOrWhiteSpace(request.Cursor);

		try
		{
			var page = await _db.Orders
				.AsNoTracking()
				.TagWith("orders.list")
				.ToCursorPageAsync(
					new CursorRequest(request.Cursor, limit),
					OrderSortKeys.CreatedAtDesc,
					o => new OrderListItemDto(
						(int)o.Id,
						o.OrderNumber.Value,
						(int)o.CustomerId,
						o.Status.ToString(),
						o.Total,
						o.Currency,
						o.CreatedAt,
						o.Items.Count),
					new PaginationOptions { IncludeTotalCount = firstPage },
					cancellationToken)
				.ConfigureAwait(false);

			return Result<PagedResult<OrderListItemDto>>.Success(page.ToPagedResult());
		}
		catch (PaginationException ex)
		{
			return Result<PagedResult<OrderListItemDto>>.Failure(
				ex.Message,
				StatusCodes.Status400BadRequest);
		}
	}
}
