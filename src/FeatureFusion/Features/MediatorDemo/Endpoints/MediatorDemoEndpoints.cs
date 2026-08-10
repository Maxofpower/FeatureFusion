using Asp.Versioning;
using BuildingBlocks.Mediator;
using FeatureFusion.Features.MediatorDemo.Commands;
using FeatureFusion.Features.MediatorDemo.Queries;
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
		var v2 = new ApiVersion(2, 0);
		var apiVersionSet = app.NewApiVersionSet()
			.HasApiVersion(v2)
			.ReportApiVersions()
			.Build();

		var api = app.MapGroup("api/v{version:apiVersion}/mediator-demo")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(v2)
			.WithTags("MediatorDemo");

		// EchoCommandValidator (FluentValidation) runs via ValidationBehavior in the mediator pipeline.
		// Empty/too-long Message → ValidationException → ValidationExceptionHandler → 400 ValidationProblemDetails.
		api.MapPost("/echo", EchoAsync)
			.WithName("MediatorDemoEcho")
			.WithSummary("Send EchoCommand through BuildingBlocks.Mediator (FluentValidation via ValidationBehavior). Use message=__throw__ to force a handler fault (500).")
			.Accepts<EchoCommand>("application/json")
			.Produces<EchoResponse>(StatusCodes.Status200OK)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status500InternalServerError);

		api.MapGet("/status", StatusAsync)
			.WithName("MediatorDemoStatus")
			.WithSummary("Send GetEchoStatusQuery through BuildingBlocks.Mediator")
			.Produces<EchoStatusResponse>(StatusCodes.Status200OK);

		return api;
	}

	private static async Task<IResult> EchoAsync(
		[FromBody] EchoCommand command,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);
		return result.Match(
			onSuccess: value => Results.Ok(value),
			onFailure: (error, statusCode) => Results.Problem(detail: error, statusCode: statusCode));
	}

	private static async Task<IResult> StatusAsync(
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new GetEchoStatusQuery(), cancellationToken).ConfigureAwait(false);
		return result.Match(
			onSuccess: value => Results.Ok(value),
			onFailure: (error, statusCode) => Results.Problem(detail: error, statusCode: statusCode));
	}
}
