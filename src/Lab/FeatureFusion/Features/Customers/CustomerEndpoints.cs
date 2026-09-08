using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.CursorPagination;
using FeatureFusion.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Customers;

/// <summary>
/// Demo Commerce customer reads. List uses BuildingBlocks.Pagination keyset;
/// customer orders use OFFSET (storefront-style nested list).
/// </summary>
public static class CustomerEndpoints
{
	public static RouteGroupBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/customers")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Customers");

		api.MapGet("/", ListCustomersAsync)
			.WithName("ListCustomers")
			.WithSummary("List customers (keyset / BuildingBlocks.Pagination). Newest first.")
			.WithDescription(
				"Uses ToCursorPageAsync with CreatedAt+Id. Distinct from storefront OFFSET catalog listing.")
			.Produces<PagedResult<CustomerListItemDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		api.MapGet("/{id:int}", GetCustomerAsync)
			.WithName("GetCustomer")
			.WithSummary("Customer detail by id.")
			.Produces<CustomerDetailDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		api.MapGet("/{id:int}/orders", ListCustomerOrdersAsync)
			.WithName("ListCustomerOrders")
			.WithSummary("Orders for a customer (OFFSET paging, newest first).")
			.Produces<CustomerOrderListDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return api;
	}

	private static async Task<IResult> ListCustomersAsync(
		ISender sender,
		CancellationToken cancellationToken,
		[FromQuery] int limit = 20,
		[FromQuery] string? cursor = null)
	{
		var result = await sender.Send(
			new ListCustomersQuery { Limit = limit, Cursor = cursor ?? string.Empty },
			cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> GetCustomerAsync(
		int id,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new GetCustomerQuery(id), cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> ListCustomerOrdersAsync(
		int id,
		ISender sender,
		CancellationToken cancellationToken,
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 20)
	{
		var result = await sender.Send(
			new ListCustomerOrdersQuery
			{
				CustomerId = id,
				Page = page,
				PageSize = pageSize
			},
			cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}
}
