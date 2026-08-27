using Enyim.Caching;
using FeatureFusion.ApiGateway.RateLimiter;
using Microsoft.Extensions.Caching.Memory;
using System.Threading.RateLimiting;

public static class MemcachedRateLimitPartition
{
	public static RateLimitPartition<TKey> GetFixedWindowRateLimiter<TKey>(
		TKey partitionKey,
		Func<TKey, MemcachedFixedWindowRateLimiterOptions> factory, 
		IMemcachedClient memcached,
		IMemoryCache memoryCache)
	{
		return RateLimitPartition.Get(partitionKey, key =>
			new MemcachedFixedWindowRateLimiter<TKey>(key, factory(key), memcached,memoryCache)
		);
	}
}