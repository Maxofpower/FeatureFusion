namespace FeatureFusion.ApiGateway.RateLimiter
{

	/// <summary>
	/// Options for the Memcached fixed window rate limiter.
	/// </summary>
	public class MemcachedFixedWindowRateLimiterOptions
	{
		public int PermitLimit { get; set; }
		public TimeSpan Window { get; set; }
	}
}
