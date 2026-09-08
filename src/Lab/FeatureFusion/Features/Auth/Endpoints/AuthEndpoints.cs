using FeatureFusion.Dtos;
using FeatureFusion.Infrastructure.Extensions;
using FeatureFusion.Services.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Auth.Endpoints;

/// <summary>JWT login for lab Feature Management demos (VIP claim).</summary>
public static class AuthEndpoints
{
	public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/Auth")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Auth");

		api.MapPost("/login", LoginAsync)
			.WithName("AuthLogin")
			.WithSummary("Issue a JWT. vipuser/vippassword receives VIP=true for the Feature Management filter preview.")
			.Accepts<LoginDto>("application/json")
			.Produces(StatusCodes.Status200OK);

		return api;
	}

	private static IResult LoginAsync([FromBody] LoginDto login, IAuthService authService)
	{
		var isVip = authService.ValidateVipUser(login.Username, login.Password);
		var token = authService.GenerateJwtToken(login.Username, isVip);
		return Results.Ok(new { token });
	}
}
