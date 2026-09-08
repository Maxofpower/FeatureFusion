using BuildingBlocks.Idempotency.AspNetCore;
using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Checkout;

/// <summary>Checkout HTTP surface nested under customers (idempotent).</summary>
public static class CheckoutEndpoints
{
	public static RouteGroupBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/customers")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Orders");

		api.MapPost("/{id:int}/checkout", CheckoutAsync)
			.WithName("Checkout")
			.WithSummary("Checkout the customer's cart (tax, shipping, demo payment, create order).")
			.WithDescription(
				"Orchestrates ITaxCalculator + IShippingPolicy + IPaymentProcessor, then Mediator CreateOrderCommand. " +
				"BuildingBlocks.Idempotency WithIdempotency(useLock: true). Same command as MCP orders.checkout. " +
				"Declined payment returns 402 without creating an order.")
			.Accepts<CheckoutCommand>("application/json")
			.Produces<CheckoutResponse>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status402PaymentRequired)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.WithIdempotency(useLock: true);

		return api;
	}

	private static async Task<IResult> CheckoutAsync(
		int id,
		[FromBody] CheckoutCommand request,
		ISender sender,
		CancellationToken cancellationToken)
	{
		request.CustomerId = id;
		var result = await sender.Send(request, cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}
}
