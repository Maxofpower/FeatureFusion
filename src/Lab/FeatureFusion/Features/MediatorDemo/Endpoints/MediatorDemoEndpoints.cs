using BuildingBlocks.Mediator;
using FeatureFusion.Features.MediatorDemo.Commands;
using FeatureFusion.Features.MediatorDemo.Queries;
using FeatureFusion.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.MediatorDemo.Endpoints;

/// <summary>
/// Vertical-slice HTTP surface for Mediator demo (command + query).
/// After calling these under Aspire, confirm ActivitySource BuildingBlocks.Mediator wraps the Send (pipeline + handler).
/// </summary>
public static class MediatorDemoEndpoints
{
	public static RouteGroupBuilder MapMediatorDemoEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/mediator-demo")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("MediatorDemo");

		api.MapPost("/echo", EchoAsync)
			.WithName("MediatorDemoEcho")
			.WithSummary("Echo a message through the mediator pipeline. Use message=__throw__ to force a handler fault (500).")
			.Accepts<EchoCommand>("application/json")
			.Produces<EchoResponse>(StatusCodes.Status200OK)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status500InternalServerError);

		api.MapGet("/status", StatusAsync)
			.WithName("MediatorDemoStatus")
			.WithSummary("Mediator query sample: echo pipeline status.")
			.Produces<EchoStatusResponse>(StatusCodes.Status200OK);

		return api;
	}

	private static async Task<IResult> EchoAsync(
		[FromBody] EchoCommand command,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> StatusAsync(
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new GetEchoStatusQuery(), cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}
}
