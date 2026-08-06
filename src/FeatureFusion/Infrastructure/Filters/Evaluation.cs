using Microsoft.FeatureManagement;

/// <summary>
/// Feature filter that enables greeting features for users with a VIP claim.
/// </summary>
/// <remarks>
/// Part of the Feature Management demos in this lab. See README and docs/linkedin-posts.md.
/// Filter alias: <c>UseGreeting</c>.
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
