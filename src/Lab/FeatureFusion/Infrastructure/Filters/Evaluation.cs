using Microsoft.FeatureManagement;

/// <summary>
/// Feature filter that enables CustomGreeting when the caller has JWT claim VIP=true.
/// </summary>
/// <remarks>
/// Lab Feature Management preview (GET /api/v1/lab/feature-filter-preview). Filter alias: <c>UseGreeting</c>.
/// </remarks>
[FilterAlias("UseGreeting")]
public class UseGreetingFilter : IFeatureFilter
{
	private readonly IHttpContextAccessor _httpContext;

	public UseGreetingFilter(IHttpContextAccessor httpContext)
	{
		_httpContext = httpContext;
	}
	public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
	{


		var httpContext = _httpContext?.HttpContext;

		if (httpContext?.User?.Claims != null)
		{
			// Check if the user has a VIP claim and if the value is "true"
			var vipClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == "VIP");

			if (vipClaim != null && vipClaim.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				// User is VIP
				return Task.FromResult(true);
			}

			// If the VIP claim is not present or not "true", return false
			return Task.FromResult(false);
		}

		return Task.FromResult(false);
	}

}
