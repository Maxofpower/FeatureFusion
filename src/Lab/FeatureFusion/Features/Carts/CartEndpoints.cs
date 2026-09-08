using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Carts;

/// <summary>Demo Commerce cart HTTP surface nested under customers.</summary>
public static class CartEndpoints
{
	public static RouteGroupBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/customers")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Customers");

		api.MapGet("/{id:int}/cart", GetCartAsync)
			.WithName("GetCart")
			.WithSummary("Get-or-create the customer's cart.")
			.Produces<CartDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		api.MapPost("/{id:int}/cart/items", AddCartItemAsync)
			.WithName("AddCartItem")
			.WithSummary("Add or increment a cart line.")
			.Accepts<AddCartItemRequest>("application/json")
			.Produces<CartDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		api.MapPut("/{id:int}/cart/items/{productId:int}", UpdateCartItemAsync)
			.WithName("UpdateCartItemQuantity")
			.WithSummary("Set quantity for a cart line (≤0 removes).")
			.Accepts<UpdateCartItemQuantityRequest>("application/json")
			.Produces<CartDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		api.MapDelete("/{id:int}/cart/items/{productId:int}", RemoveCartItemAsync)
			.WithName("RemoveCartItem")
			.WithSummary("Remove a product line from the cart.")
			.Produces<CartDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		api.MapDelete("/{id:int}/cart", ClearCartAsync)
			.WithName("ClearCart")
			.WithSummary("Clear all lines from the customer's cart.")
			.Produces<CartDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return api;
	}

	private static async Task<IResult> GetCartAsync(
		int id,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new GetCartQuery(id), cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> AddCartItemAsync(
		int id,
		[FromBody] AddCartItemRequest body,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(
			new AddCartItemCommand(id, body.ProductId, body.Quantity),
			cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> UpdateCartItemAsync(
		int id,
		int productId,
		[FromBody] UpdateCartItemQuantityRequest body,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(
			new UpdateCartItemQuantityCommand(id, productId, body.Quantity),
			cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> RemoveCartItemAsync(
		int id,
		int productId,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(
			new RemoveCartItemCommand(id, productId),
			cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> ClearCartAsync(
		int id,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new ClearCartCommand(id), cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}
}
