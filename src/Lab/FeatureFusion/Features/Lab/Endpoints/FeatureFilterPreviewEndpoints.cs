using FeatureFusion.Infrastructure.Extensions;
using Microsoft.FeatureManagement;

namespace FeatureFusion.Features.Lab.Endpoints;

/// <summary>
/// Dedicated Feature Management showcase: CustomGreeting flag + UseGreeting filter (VIP claim).
/// </summary>
public static class FeatureFilterPreviewEndpoints
{
	private const string Description =
		"Feature Management lab preview (not a storefront API). " +
		"Evaluates feature flag **CustomGreeting** via filter alias **UseGreeting**, which returns true when the " +
		"JWT has claim VIP=true. Obtain a token from POST /api/v1/Auth/login (vipuser/vippassword for VIP). " +
		"Anonymous or non-VIP callers get the anonymous message. " +
		"Capabilities demonstrated: Microsoft.FeatureManagement feature flags, custom IFeatureFilter, JWT claim evaluation.";

	public static RouteGroupBuilder MapFeatureFilterPreviewEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/lab")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Lab — Feature Management");

		api.MapGet("/feature-filter-preview", PreviewAsync)
			.WithName("FeatureFilterPreview")
			.WithSummary("Feature filter preview: CustomGreeting + UseGreeting (VIP claim).")
			.WithDescription(Description)
			.Produces<string>(StatusCodes.Status200OK);

		return api;
	}

	private static async Task<IResult> PreviewAsync(IFeatureManager featureManager, HttpContext httpContext)
	{
		var name = httpContext.User.Identity?.Name ?? "caller";
		if (await featureManager.IsEnabledAsync("CustomGreeting").ConfigureAwait(false))
			return Results.Ok($"Hello VIP user {name}, CustomGreeting is enabled via UseGreeting filter.");

		return Results.Ok($"Hello Anonymous user {name}, CustomGreeting is disabled for this caller.");
	}
}
