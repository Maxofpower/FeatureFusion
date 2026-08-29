using System.Collections.Concurrent;
using System.Text.Json;

namespace BuildingBlocks.Mcp;

/// <summary>
/// In-memory <see cref="IMcpIdempotencyStore"/> for single-instance hosts and tests.
/// Keys should already be namespaced by the invoker (<c>toolName + key</c>). Optional TTL; expired entries are ignored.
/// Not a distributed store — register Redis (or similar) in production farms.
/// </summary>
public sealed class MemoryIdempotencyStore : IMcpIdempotencyStore
{
	private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);
	private readonly TimeSpan? _timeToLive;

	/// <summary>Creates a store with no expiry.</summary>
	public MemoryIdempotencyStore()
		: this(timeToLive: null)
	{
	}

	/// <summary>Creates a store. Positive <paramref name="timeToLive"/> expires entries after that duration.</summary>
	public MemoryIdempotencyStore(TimeSpan? timeToLive)
	{
		_timeToLive = timeToLive is { } t && t > TimeSpan.Zero ? t : null;
	}

	/// <inheritdoc />
	public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		if (!_store.TryGetValue(key, out var entry))
			return Task.FromResult<string?>(null);
		if (entry.ExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
		{
			_store.TryRemove(key, out _);
			return Task.FromResult<string?>(null);
		}

		return Task.FromResult<string?>(entry.PayloadJson);
	}

	/// <inheritdoc />
	public Task SetAsync(string key, string payloadJson, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentNullException.ThrowIfNull(payloadJson);
		DateTimeOffset? expires = _timeToLive is { } ttl ? DateTimeOffset.UtcNow.Add(ttl) : null;
		_store[key] = new Entry(payloadJson, expires);
		return Task.CompletedTask;
	}

	private readonly record struct Entry(string PayloadJson, DateTimeOffset? ExpiresAt);
}

/// <summary>
/// Rate limiter that always allows. Register a real implementation to protect write tools from agent storms.
/// </summary>
public sealed class NoOpRateLimiter : IMcpRateLimiter
{
	/// <inheritdoc />
	public ValueTask<McpRateLimitDecision> TryAcquireAsync(
		string toolName,
		McpInvokeContext context,
		CancellationToken cancellationToken)
		=> ValueTask.FromResult(McpRateLimitDecision.Allow);
}

/// <summary>
/// Default mapper: pass-through <see cref="McpResult{T}"/>, duck-typed <c>IsSuccess</c>/<c>Error</c>/<c>Value</c>, otherwise success.
/// </summary>
public sealed class DefaultMcpResultMapper : IMcpResultMapper
{
	/// <inheritdoc />
	public McpResult<object?> Map(object? handlerResult)
	{
		if (handlerResult is null)
			return McpResult.Ok<object?>(null);

		var type = handlerResult.GetType();
		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(McpResult<>))
		{
			var isSuccess = (bool)type.GetProperty(nameof(McpResult<object>.IsSuccess))!.GetValue(handlerResult)!;
			if (isSuccess)
				return McpResult.Ok<object?>(type.GetProperty(nameof(McpResult<object>.Value))!.GetValue(handlerResult));
			var error = (McpError)type.GetProperty(nameof(McpResult<object>.Error))!.GetValue(handlerResult)!;
			return McpResult.Fail<object?>(error);
		}

		var isSuccessProp = type.GetProperty("IsSuccess");
		var errorProp = type.GetProperty("Error");
		var valueProp = type.GetProperty("Value");
		if (isSuccessProp?.PropertyType == typeof(bool) && errorProp is not null && valueProp is not null)
		{
			var ok = (bool)isSuccessProp.GetValue(handlerResult)!;
			if (ok)
				return McpResult.Ok<object?>(valueProp.GetValue(handlerResult));
			var err = errorProp.GetValue(handlerResult)?.ToString() ?? "The operation failed.";
			var status = type.GetProperty("StatusCode")?.GetValue(handlerResult) as int?;
			return McpResult.Fail<object?>(new McpError(McpErrorCode.Validation, err, httpStatusHint: status is > 0 ? status : 400));
		}

		return McpResult.Ok<object?>(handlerResult);
	}
}

/// <summary>
/// Serializes success payloads for the idempotency cache.
/// </summary>
internal static class McpJson
{
	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};
}
