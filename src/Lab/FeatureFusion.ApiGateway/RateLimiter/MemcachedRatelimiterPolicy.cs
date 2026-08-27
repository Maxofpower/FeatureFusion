using Enyim.Caching;
using FeatureFusion.ApiGateway.RateLimiter;
using FeatureFusion.ApiGateway.RateLimiter.Enums;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.RateLimiting;

public class MemcachedRateLimiterPolicy : IRateLimiterPolicy<string>
{
	private readonly IMemcachedClient _memcached;
	private readonly IMemoryCache _memoryCache; // for fallback on memcached failor

	public MemcachedRateLimiterPolicy(IMemcachedClient memcached
		,IMemoryCache memoryCache)
	{
		_memcached = memcached ?? throw new ArgumentNullException(nameof(memcached));
		_memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
	}

	public RateLimitPartition<string> GetPartition(HttpContext httpContext)
	{
		// Creating the partition key using the policy name and client IP (It can be tenantId,userid,etc).
		//TODO: Configuration should be mapped from appsettings
		var partitionKey = $"{RateLimiterPolicy.MemcachedFixedWindow}-{httpContext.Connection.RemoteIpAddress}";

		return MemcachedRateLimitPartition.GetFixedWindowRateLimiter( 
			partitionKey: partitionKey,
			factory: _ => new MemcachedFixedWindowRateLimiterOptions
			{
				PermitLimit = 10,
				Window = TimeSpan.FromMinutes(1)
			},
			memcached: _memcached,
			memoryCache: _memoryCache
		);
	}
	public Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected
	{
		get => (context, cancellationToken) =>
		{
			// Custom behavior when the rate limit is exceeded.
			context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
			context.HttpContext.Response.WriteAsync("Too Many Requests. Please try again later.", cancellationToken);

			return ValueTask.CompletedTask;
		};
	}
}