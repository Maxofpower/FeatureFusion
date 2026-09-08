using BuildingBlocks.Idempotency.AspNetCore;
using BuildingBlocks.Mcp;
using BuildingBlocks.Mcp.Hosting;
using FeatureFusion.Dtos;
using FeatureFusion.Infrastructure.Extensions;
using FeatureFusion.Services.ProductService;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement;

namespace FeatureFusion.Features.Lab.Endpoints;

/// <summary>Lab Minimal APIs (validation samples, cache demos, ping, idempotency smoke).</summary>
public static class LabEndpoints
{
	public static RouteGroupBuilder MapLabEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Lab");

		api.MapGet("/product-promotion", GetProductPromotion)
			.WithName("GetProductPromotion")
			.WithSummary("Lab cache demo: product promotions (not Feature Management showcase).");

		api.MapGet("/product-recommendation", GetProductRecommendation)
			.WithName("GetProductRecommendation")
			.WithSummary("Lab cache middleware demo: product recommendations (not Feature Management showcase).");

		api.MapPost("/minimal-custom-greeting", GetCustomGreeting)
			.WithName("MinimalCustomGreeting")
			.WithSummary("Lab FluentValidation demo (GreetingDto). Not the Feature Management filter preview.")
			.WithDescription(
				"Validates Fullname via GreetingValidator. For CustomGreeting / UseGreeting filter behavior, " +
				"use GET /api/v1/lab/feature-filter-preview.")
			.Produces<Ok<string>>()
			.ProducesValidationProblem()
			.Produces<NotFound<string>>();

		api.MapPost("/person-endpointfilter", HandleCreatePerson)
			.WithName("PersonEndpointFilter")
			.WithSummary("Lab validation via AddEndpointFilter (not Feature Management showcase).")
			.AddEndpointFilter<ValidationFilter<PersonDto>>();

		api.MapPost("/person-builderextension", HandleCreatePerson)
			.WithName("PersonBuilderExtension")
			.WithSummary("Lab validation via WithValidation extension (not Feature Management showcase).")
			.WithValidation<PersonDto>();

		api.MapPostWithValidation<PersonDto>("/person-genericendpoint", HandleCreatePerson)
			.WithName("PersonGenericEndpoint")
			.WithSummary("Lab validation via MapPostWithValidation (not Feature Management showcase).");

		api.MapGet("/lab-ping", LabPing)
			.WithName("LabPing")
			.WithSummary("Minimal API ping (not a Mediator command). Same method is MCP tool lab.ping.")
			.WithMcp(app);

		api.MapPost("/idempotency-smoke", () => Results.Ok(new { ok = true }))
			.WithName("IdempotencySmoke")
			.WithSummary("Minimal API Idempotency-Key smoke (WithIdempotency).")
			.WithIdempotency(useLock: true);

		return api;
	}

	public static async Task<Results<Ok<IList<ProductPromotionDto>>, NotFound<string>>> GetProductPromotion(
		IProductService productService,
		bool getFromMemCach = false)
	{
		try
		{
			var promotions = await productService.GetProductPromotionAsync(getFromMemCach);
			return TypedResults.Ok(promotions);
		}
		catch
		{
			return TypedResults.NotFound("An error occurred while fetching promotions.");
		}
	}

	public static async Task<Results<Ok<List<ProductPromotionDto>>, NotFound<string>>> GetProductRecommendation(
		IProductService productService)
	{
		try
		{
			var recommendation = await productService.GetProductRocemmendationAsync();
			return TypedResults.Ok(recommendation);
		}
		catch
		{
			return TypedResults.NotFound("An error occurred while fetching promotions.");
		}
	}

	public static async Task<Results<Ok<string>, BadRequest<ValidationProblemDetails>, NotFound<string>>> GetCustomGreeting(
		[AsParameters] GreetingDto greeting,
		GreetingValidator validator,
		ILogger<GreetingValidator> logger)
	{
		var validationResult = await validator.ValidateWithResultAsync(greeting);
		if (!validationResult.IsValid)
		{
			logger.LogWarning("validation error on {GreetingType}: {Errors}",
				nameof(GreetingDto), validationResult.ProblemDetails!.Errors);
			return TypedResults.BadRequest(validationResult.ProblemDetails);
		}

		return TypedResults.Ok($"Hello {greeting.Fullname}, greeting validated.");
	}

	public static Task<Results<Ok<string>, BadRequest<ValidationProblemDetails>, NotFound<string>>> HandleCreatePerson(
		[AsParameters] PersonDto person)
	{
		return Task.FromResult<Results<Ok<string>, BadRequest<ValidationProblemDetails>, NotFound<string>>>(
			TypedResults.Ok($"Hello {person.Name}"));
	}

	/// <summary>
	/// HTTP GET <c>/api/v1/lab-ping</c> and MCP tool <c>lab.ping</c> — same method, not a Mediator command.
	/// </summary>
	[McpTool("lab.ping", Description = "Minimal API ping (not a Mediator command)", Kind = McpToolKind.Query)]
	public static string LabPing([AsParameters] LabPingRequest request)
		=> string.IsNullOrWhiteSpace(request.Name) ? "pong" : $"pong:{request.Name}";
}

public sealed class LabPingRequest
{
	public string Name { get; set; } = default!;
}
