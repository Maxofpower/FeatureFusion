using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Orders;

public sealed class GetOrderQueryHandler
	: IQueryHandler<GetOrderQuery, Result<OrderDetailDto>>
{
	private readonly CatalogDbContext _db;

	public GetOrderQueryHandler(CatalogDbContext db) => _db = db;

	public async Task<Result<OrderDetailDto>> Handle(
		GetOrderQuery request,
		CancellationToken cancellationToken)
	{
		var order = await _db.Orders.AsNoTracking()
			.Where(o => (int)o.Id == request.Id)
			.Select(o => new
			{
				Id = (int)o.Id,
				OrderNumber = o.OrderNumber.Value,
				CustomerId = (int)o.CustomerId,
				CustomerEmail = o.Customer != null ? o.Customer.Email.Value : null,
				CustomerDisplayName = o.Customer != null ? o.Customer.DisplayName : null,
				Status = o.Status.ToString(),
				o.Subtotal,
				o.TaxAmount,
				o.ShippingAmount,
				o.Total,
				o.Currency,
				o.CreatedAt,
				Lines = o.Items
					.OrderBy(i => (int)i.Id)
					.Select(i => new OrderLineDto(
						(int)i.ProductId,
						i.Product != null ? i.Product.Name : null,
						i.Product != null ? i.Product.Slug.Value : null,
						i.Product != null ? i.Product.Sku.Value : null,
						i.Quantity,
						i.UnitPrice,
						i.UnitPrice * i.Quantity))
					.ToList()
			})
			.SingleOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

		if (order is null)
			return Result<OrderDetailDto>.Failure("Order not found.", StatusCodes.Status404NotFound);

		return Result<OrderDetailDto>.Success(new OrderDetailDto(
			order.Id,
			order.OrderNumber,
			order.CustomerId,
			order.CustomerEmail,
			order.CustomerDisplayName,
			order.Status,
			order.Subtotal,
			order.TaxAmount,
			order.ShippingAmount,
			order.Total,
			order.Currency,
			order.CreatedAt,
			order.Lines));
	}
}
