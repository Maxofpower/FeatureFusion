using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Customers;

public sealed class GetCustomerQueryHandler
	: IQueryHandler<GetCustomerQuery, Result<CustomerDetailDto>>
{
	private readonly CatalogDbContext _db;

	public GetCustomerQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<CustomerDetailDto>> Handle(
		GetCustomerQuery request,
		CancellationToken cancellationToken)
	{
		var customer = await _db.Customers.AsNoTracking()
			.Where(c => (int)c.Id == request.Id)
			.Select(c => new CustomerDetailDto(
				(int)c.Id,
				c.Email.Value,
				c.DisplayName,
				c.CreatedAt))
			.SingleOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

		if (customer is null)
			return Result<CustomerDetailDto>.Failure("Customer not found.", StatusCodes.Status404NotFound);

		return Result<CustomerDetailDto>.Success(customer);
	}
}
