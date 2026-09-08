using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.CursorPagination;
using FeatureFusion.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Orders;

/// <summary>
/// Demo Commerce order reads under <c>/api/v1/orders</c>.
/// Create remains at <c>POST /api/v1/Order/order</c> (experiment path).
/// </summary>
public static class OrderQueryEndpoints
{
	public static RouteGroupBuilder MapOrderQueryEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/orders")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Orders");

		api.MapGet("/", ListOrdersAsync)
			.WithName("ListOrders")
			.WithSummary("List orders (keyset / BuildingBlocks.Pagination). Newest first.")
			.Produces<PagedResult<OrderListItemDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		api.MapGet("/{id:int}", GetOrderAsync)
			.WithName("GetOrder")
			.WithSummary("Order detail with lines and product references.")
			.Produces<OrderDetailDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return api;
	}

	private static async Task<IResult> ListOrdersAsync(
		ISender sender,
		CancellationToken cancellationToken,
		[FromQuery] int limit = 20,
		[FromQuery] string? cursor = null)
	{
		var result = await sender.Send(
			new ListOrdersQuery { Limit = limit, Cursor = cursor ?? string.Empty },
			cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> GetOrderAsync(
		int id,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new GetOrderQuery(id), cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}
}
