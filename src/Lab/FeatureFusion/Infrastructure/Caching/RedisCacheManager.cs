using Enyim.Caching;
using FeatureFusion.Infrastructure.Caching;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace FeatureFusion.Infrastructure.Caching
{
	internal class RedisCacheManager : IDistributedCacheManager
	{

		protected readonly IRedisConnectionWrapper _connectionWrapper;
		protected readonly ILogger<MemcachedCacheManager> _logger;
		private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
		public RedisCacheManager(IRedisConnectionWrapper connectionWrapper,
		ILogger<MemcachedCacheManager> logger)
		{
			_connectionWrapper = connectionWrapper;
			_logger = logger;
		}
		public async Task<T> GetValueOrCreateAsync<T>(CacheKey key, Func<Task<T>> acquire)
		{

			var database = await _connectionWrapper.GetDatabaseAsync();
			string redisKey = key.ToString();

			// Lua script: Atomically get existing value or set if missing
			var luaScript = @"
        local value = redis.call('GET', KEYS[1])
        if value then
            return value
        else
            redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[2])
            return ARGV[1]
        end";

			var cachedData = (string)await database.ScriptEvaluateAsync(luaScript,
				new RedisKey[] { redisKey },
				new RedisValue[] { JsonSerializer.Serialize(await acquire()), key.CacheTime * 60 });

			return cachedData is not null ? JsonSerializer.Deserialize<T>(cachedData) : default!;
		}

		public async Task RefreshCacheAsync<T>(string key, Func<Task<T>> fetchFromDb, int cacheMinutes)
		{
			var database = await _connectionWrapper.GetDatabaseAsync();

			// Fetch fresh data from source
			var newData = await fetchFromDb();
			if (newData != null)
			{
				var serializedData = JsonSerializer.Serialize(newData);
				await database.StringSetAsync(key, serializedData, TimeSpan.FromMinutes(cacheMinutes));
			}
		}

		public Task<T> GetValueOrCreateAsync<T>(CacheKey key, Func<Task<T>> acquire, CancellationToken cancellationToken = default)
		{
			// I currently using DistributedCache from microsoft for redis not manual one
			// TODO: implement 

			throw new NotImplementedException();
		}

		public Task RemoveAsync(string cacheKey, CancellationToken token)
		{
			// I currently using DistributedCache from microsoft for redis not manual one
			// TODO: implement
			throw new NotImplementedException();
		}
	}
}


