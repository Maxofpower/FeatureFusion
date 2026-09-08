using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Customers;

public sealed class ListCustomerOrdersQueryHandler
	: IQueryHandler<ListCustomerOrdersQuery, Result<CustomerOrderListDto>>
{
	private readonly CatalogDbContext _db;

	public ListCustomerOrdersQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<CustomerOrderListDto>> Handle(
		ListCustomerOrdersQuery request,
		CancellationToken cancellationToken)
	{
		var exists = await _db.Customers.AsNoTracking()
			.AnyAsync(c => (int)c.Id == request.CustomerId, cancellationToken)
			.ConfigureAwait(false);
		if (!exists)
			return Result<CustomerOrderListDto>.Failure(
				"Customer not found.",
				StatusCodes.Status404NotFound);

		var page = request.Page < 1 ? 1 : request.Page;
		var pageSize = request.PageSize is < 1 or > 50 ? 20 : request.PageSize;

		var query = _db.Orders.AsNoTracking()
			.Where(o => (int)o.CustomerId == request.CustomerId);

		var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
		var items = await query
			.OrderByDescending(o => o.CreatedAt)
			.ThenByDescending(o => (int)o.Id)
			.Skip((page - 1) * pageSize)
			.Take(pageSize)
			.Select(o => new CustomerOrderListItemDto(
				(int)o.Id,
				o.OrderNumber.Value,
				o.Status.ToString(),
				o.Total,
				o.Currency,
				o.CreatedAt,
				o.Items.Count))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return Result<CustomerOrderListDto>.Success(
			new CustomerOrderListDto(items, page, pageSize, total));
	}
}
