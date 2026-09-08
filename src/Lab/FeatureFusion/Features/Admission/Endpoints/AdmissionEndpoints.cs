using FeatureFusion.Features.Admission;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Features.Admission.Endpoints;

/// <summary>Trusted HTTP release surface for deferred capability tickets (not an MCP tool).</summary>
public static class AdmissionEndpoints
{
	public static RouteGroupBuilder MapAdmissionEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/admission")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Admission");

		api.MapPost("/tickets/{ticketId:guid}/release", ReleaseAsync)
			.WithName("ReleaseIntentTicket")
			.WithSummary("Release a Pending intent ticket and execute the deferred capability (trusted HTTP only).")
			.Produces(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status410Gone);

		api.MapGet("/tickets/{ticketId:guid}", GetAsync)
			.WithName("GetIntentTicket")
			.WithSummary("Inspect an intent ticket.")
			.Produces(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return api;
	}

	private static async Task<IResult> ReleaseAsync(
		Guid ticketId,
		ICapabilityAdmission admission,
		[FromHeader(Name = "X-Released-By")] string? releasedBy,
		CancellationToken cancellationToken)
	{
		var result = await admission.ReleaseAsync(ticketId, releasedBy, cancellationToken).ConfigureAwait(false);
		if (!result.Succeeded)
		{
			return Results.Problem(
				detail: result.Error,
				statusCode: result.StatusCode,
				title: "Admission release failed");
		}

		return Results.Ok(new
		{
			ticketId = result.TicketId,
			orderId = result.OrderId,
			status = nameof(IntentTicketStatus.Released),
			outcome = "Released"
		});
	}

	private static async Task<IResult> GetAsync(
		Guid ticketId,
		CatalogDbContext db,
		CancellationToken cancellationToken)
	{
		var ticket = await db.IntentTickets
			.AsNoTracking()
			.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
			.ConfigureAwait(false);

		if (ticket is null)
			return Results.NotFound();

		return Results.Ok(new
		{
			ticketId = ticket.Id,
			capabilityId = ticket.CapabilityId,
			status = ticket.Status.ToString(),
			createdAt = ticket.CreatedAt,
			expiresAt = ticket.ExpiresAt,
			releasedAt = ticket.ReleasedAt,
			releasedBy = ticket.ReleasedBy,
			executionOrderId = ticket.ExecutionOrderId
		});
	}
}
