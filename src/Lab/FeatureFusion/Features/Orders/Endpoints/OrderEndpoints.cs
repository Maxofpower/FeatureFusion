using BuildingBlocks.Idempotency.AspNetCore;
using BuildingBlocks.Mediator;
using FeatureFusion.Features.Admission;
using FeatureFusion.Features.Orders.Commands;
using FeatureFusion.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Orders.Endpoints;

/// <summary>
/// Order create HTTP surface (idempotency + admission + mediator).
/// Path kept as <c>POST /api/v1/Order/order</c> for experiment contracts.
/// </summary>
public static class OrderEndpoints
{
	public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/Order")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Orders");

		api.MapPost("/order", CreateOrderAsync)
			.WithName("CreateOrder")
			.WithSummary("Create a catalog order. Requires Idempotency-Key; may return 202 when admission defers.")
			.WithDescription(
				"FluentValidation → capability admission (Allow / Defer / Deny) → Mediator CreateOrderCommand. " +
				"BuildingBlocks.Idempotency WithIdempotency(useLock: true). Same command as MCP orders.create. " +
				"Persists Domain.Orders.Order with catalog price snapshots (not a checkout).")
			.Accepts<CreateOrderCommand>("application/json")
			.Produces<OrderResponse>(StatusCodes.Status200OK)
			.Produces<AdmissionPendingResponse>(StatusCodes.Status202Accepted)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.WithIdempotency(useLock: true);

		return api;
	}

	private static async Task<IResult> CreateOrderAsync(
		[FromBody] CreateOrderCommand request,
		OrderRequestValidator validator,
		ISender sender,
		ICapabilityAdmission admission,
		HttpRequest httpRequest,
		CancellationToken cancellationToken)
	{
		var validationResult = await validator.ValidateWithResultAsync(request).ConfigureAwait(false);
		if (!validationResult.IsValid)
			return Results.BadRequest(validationResult.ProblemDetails);

		var requestKey = OrderCreateAdmissionGate.ResolveHttpRequestKey(httpRequest);
		var decision = await OrderCreateAdmissionGate.AdmitCreateOrderAsync(
			admission,
			request,
			requestKey,
			cancellationToken).ConfigureAwait(false);

		switch (decision)
		{
			case AdmissionDecision.Defer defer:
				return Results.Accepted(value: defer.Pending);
			case AdmissionDecision.Deny deny:
				return Results.Problem(
					title: "Admission denied",
					detail: deny.Error,
					statusCode: deny.StatusCode);
			case AdmissionDecision.Allow:
				break;
			default:
				return Results.Problem("Unknown admission decision.", statusCode: StatusCodes.Status500InternalServerError);
		}

		var createOrderResult = await sender.Send(request, cancellationToken).ConfigureAwait(false);
		return createOrderResult.Match(
			onSuccess: value => Results.Ok(value),
			onFailure: (error, statusCode) => Results.Problem(
				detail: error,
				statusCode: statusCode is >= 400 and < 600 ? statusCode : StatusCodes.Status400BadRequest));
	}
}
