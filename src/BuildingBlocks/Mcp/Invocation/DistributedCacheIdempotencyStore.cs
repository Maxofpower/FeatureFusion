using Microsoft.Extensions.Caching.Distributed;

namespace BuildingBlocks.Mcp;

/// <summary>
/// <see cref="IMcpIdempotencyStore"/> over host <see cref="IDistributedCache"/>.
/// Prefixes keys with <see cref="McpDefaults.IdempotencyPayloadKeyPrefix"/> so MCP payloads
/// do not collide with HTTP <c>Idempotency_*</c> entries. This is completed-payload storage only —
/// in-flight serialization requires <see cref="IMcpIdempotencyLock"/>.
/// </summary>
public sealed class DistributedCacheIdempotencyStore : IMcpIdempotencyStore
{
	private readonly IDistributedCache _cache;
	private readonly TimeSpan? _payloadTtl;

	/// <summary>Creates a store. <paramref name="payloadTtl"/> of zero or less is treated as no absolute expiry.</summary>
	public DistributedCacheIdempotencyStore(IDistributedCache cache, TimeSpan? payloadTtl = null)
	{
		_cache = cache ?? throw new ArgumentNullException(nameof(cache));
		_payloadTtl = payloadTtl is { } t && t > TimeSpan.Zero ? t : null;
	}

	/// <inheritdoc />
	public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		return await _cache.GetStringAsync(ToCacheKey(key), cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task SetAsync(string key, string payloadJson, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentNullException.ThrowIfNull(payloadJson);

		DistributedCacheEntryOptions? options = null;
		if (_payloadTtl is { } ttl)
			options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };

		await _cache.SetStringAsync(ToCacheKey(key), payloadJson, options ?? new DistributedCacheEntryOptions(), cancellationToken)
			.ConfigureAwait(false);
	}

	private static string ToCacheKey(string invokerKey)
		=> invokerKey.StartsWith(McpDefaults.IdempotencyPayloadKeyPrefix, StringComparison.Ordinal)
			? invokerKey
			: McpDefaults.IdempotencyPayloadKeyPrefix + invokerKey;
}
