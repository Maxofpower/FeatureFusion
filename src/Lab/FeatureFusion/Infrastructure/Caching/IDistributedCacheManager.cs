using FeatureFusion.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Hybrid;

public interface IDistributedCacheManager
{
	Task<T> GetValueOrCreateAsync<T>(CacheKey key, Func<Task<T>> acquire,CancellationToken cancellationToken=default);
	Task RefreshCacheAsync<T>(string key, Func<Task<T>> fetchFromDb, int cacheMinutes);
	Task RemoveAsync(string cacheKey, CancellationToken token);

}