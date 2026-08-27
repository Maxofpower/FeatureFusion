using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace FeatureFusion.ApiGateway.RateLimiter.Enums
{
	public enum RateLimiterPolicy
	{
		[Display(Name ="MemcachedFixedWindow")]
		MemcachedFixedWindow
	}
}
