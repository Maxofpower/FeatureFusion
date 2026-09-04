using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BuildingBlocks.Idempotency;

/// <summary>
/// Shared idempotency lifecycle over <see cref="IDistributedCache"/> with optional <see cref="IIdempotencyLock"/>.
/// </summary>
/// <remarks>
/// MVC and Minimal API hosts call <see cref="BeginAsync"/>, then <see cref="CompleteAsync"/> or
/// <see cref="AbandonAsync"/>. This type does not produce HTTP results — map via
/// <see cref="IdempotencyProblemDetails"/> or replay <see cref="IdempotencyEnvelope"/>.
/// </remarks>
public sealed class IdempotencyGate
{
	private readonly IDistributedCache _distributedCache;
	private readonly ILogger _logger;
	private readonly IIdempotencyLock? _idempotencyLock;
	private readonly IdempotencyOptions _options;

	/// <summary>Creates a gate.</summary>
	/// <exception cref="ArgumentNullException">Required dependency is null.</exception>
	public IdempotencyGate(
		IDistributedCache distributedCache,
		ILogger logger,
		IdempotencyOptions options,
		IIdempotencyLock? idempotencyLock = null)
	{
		_distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_idempotencyLock = idempotencyLock;
	}

	/// <summary>
	/// Validates the key, resolves identity, and GetOrCreates the cache entry.
	/// Returns <see cref="IdempotencyDecisionKind.Execute"/> when the caller owns the lease.
	/// </summary>
	public async Task<IdempotencyDecision> BeginAsync(
		HttpContext httpContext,
		IdempotencyEndpointSettings endpoint,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(httpContext);
		ArgumentNullException.ThrowIfNull(endpoint);

		var headerName = _options.HeaderName;
		string? rawKey = null;
		if (httpContext.Request.Headers.TryGetValue(headerName, out var headerValue))
			rawKey = headerValue.ToString();

		if (!IdempotencyKeyValidator.TryValidate(rawKey, _options, out var keyError))
			return IdempotencyDecision.BadKey(keyError!);

		if (!TryResolveUserId(httpContext, out var userId, out var authError))
			return IdempotencyDecision.Unauthorized(authError!);

		var cacheKey = BuildCacheKey(httpContext, userId!, rawKey!);
		string? requestFingerprint = null;

		try
		{
			if (_options.EnableRequestFingerprint)
			{
				var request = httpContext.Request;
				request.EnableBuffering();
				if (request.Body.CanSeek)
					request.Body.Position = 0;

				requestFingerprint = await IdempotencyFingerprint
					.ComputeAsync(request.Method, request.Path.Value ?? string.Empty, request.Body, cancellationToken)
					.ConfigureAwait(false);

				if (request.Body.CanSeek)
					request.Body.Position = 0;
			}

			var (isNewlyCreated, cacheEntry) = await GetOrCreateCacheEntryAsync(
					cacheKey,
					requestFingerprint,
					endpoint,
					cancellationToken)
				.ConfigureAwait(false);

			if (cacheEntry is not null && !isNewlyCreated)
			{
				if (_options.EnableRequestFingerprint
					&& !string.IsNullOrEmpty(cacheEntry.RequestFingerprint)
					&& requestFingerprint is not null
					&& !IdempotencyFingerprint.FixedTimeEquals(cacheEntry.RequestFingerprint, requestFingerprint))
				{
					return IdempotencyDecision.FingerprintConflict(
						cacheKey,
						"Idempotency-Key was reused with a different request payload.");
				}

				if (cacheEntry.Status == "Processing")
				{
					return IdempotencyDecision.ProcessingConflict(
						cacheKey,
						"Request is already being processed. Please wait.");
				}

				if (cacheEntry.Status is "Completed" or "Failed")
				{
					if (_options.DuplicateCompletedBehavior == DuplicateCompletedBehavior.Conflict)
					{
						return IdempotencyDecision.DuplicateConflict(
							cacheKey,
							"Idempotency-Key was already used for a completed request.");
					}

					var envelope = new IdempotencyEnvelope(
						cacheEntry.StatusCode ?? StatusCodes.Status200OK,
						string.IsNullOrEmpty(cacheEntry.ContentType) ? "application/json" : cacheEntry.ContentType,
						cacheEntry.Response ?? string.Empty);
					return IdempotencyDecision.Replay(cacheKey, envelope);
				}

				return IdempotencyDecision.LockFailure(
					cacheKey,
					$"Unknown cache status: {cacheEntry.Status}");
			}

			await MarkRequestAsProcessingAsync(cacheKey, requestFingerprint, endpoint, cancellationToken)
				.ConfigureAwait(false);

			return IdempotencyDecision.Execute(cacheKey, requestFingerprint);
		}
		catch (TimeoutException ex)
		{
			_logger.LogError(ex, "Idempotency cache operation timed out for key {CacheKey}.", cacheKey);
			return IdempotencyDecision.LockFailure(cacheKey, ex.Message);
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogError(ex, "Idempotency lock or cache failure for key {CacheKey}.", cacheKey);
			return IdempotencyDecision.LockFailure(cacheKey, ex.Message);
		}
	}

	/// <summary>Stores a successful 2xx envelope as Completed.</summary>
	public async Task CompleteAsync(
		string cacheKey,
		IdempotencyEnvelope envelope,
		string? requestFingerprint,
		IdempotencyEndpointSettings endpoint,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(cacheKey);
		ArgumentNullException.ThrowIfNull(envelope);
		ArgumentNullException.ThrowIfNull(endpoint);

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(_options.CacheWriteTimeout);

		var cacheEntry = new IdempotencyCacheEntry
		{
			Status = "Completed",
			Response = envelope.Body,
			StatusCode = envelope.StatusCode,
			ContentType = envelope.ContentType,
			RequestFingerprint = requestFingerprint,
			ProcessingExpiresAtUtc = null
		};
		var cacheEntryData = JsonConvert.SerializeObject(cacheEntry);
		var entryTtl = endpoint.EntryTtl ?? _options.EntryTtl;

		await _distributedCache.SetAsync(
			cacheKey,
			Encoding.UTF8.GetBytes(cacheEntryData),
			new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = entryTtl },
			cts.Token).ConfigureAwait(false);
	}

	/// <summary>Removes the cache entry so the key may be retried.</summary>
	public async Task AbandonAsync(
		string cacheKey,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(cacheKey);

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(_options.CacheWriteTimeout);
		await _distributedCache.RemoveAsync(cacheKey, cts.Token).ConfigureAwait(false);
	}

	private bool TryResolveUserId(HttpContext httpContext, out string? userId, out string? error)
	{
		var claimValue = httpContext.User.FindFirst(_options.UserIdClaimType)?.Value;
		userId = claimValue ?? _options.UserIdFallback;

		if (string.IsNullOrEmpty(userId))
		{
			error = "User ID is missing in the request context.";
			return false;
		}

		error = null;
		return true;
	}

	private string BuildCacheKey(HttpContext httpContext, string userId, string idempotencyKey)
	{
		var segments = new List<string> { _options.KeyPrefix };
		foreach (var claimType in _options.KeyScopeClaimTypes)
		{
			var value = httpContext.User.FindFirst(claimType)?.Value;
			if (!string.IsNullOrEmpty(value))
				segments.Add(value);
		}

		segments.Add(userId);
		segments.Add(idempotencyKey);
		return string.Join('_', segments);
	}

	private async Task<(bool, IdempotencyCacheEntry?)> GetOrCreateCacheEntryAsync(
		string cacheKey,
		string? requestFingerprint,
		IdempotencyEndpointSettings endpoint,
		CancellationToken cancellationToken)
	{
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(_options.CacheOperationTimeout);

		try
		{
			if (endpoint.UseLock)
			{
				if (_idempotencyLock is null)
					throw new InvalidOperationException(
						"UseLock=true requires a registered IIdempotencyLock.");

				var lockKey = $"{cacheKey}_lock";
				var lockValue = Guid.NewGuid().ToString();

				var lockAcquired = await _idempotencyLock
					.AcquireAsync(lockKey, lockValue, _options.LockExpiry, cts.Token)
					.ConfigureAwait(false);
				if (!lockAcquired)
					throw new InvalidOperationException("Failed to acquire Redis lock.");

				try
				{
					return await GetOrCreateCacheEntryWithoutLockAsync(
							cacheKey,
							requestFingerprint,
							endpoint,
							cts.Token)
						.ConfigureAwait(false);
				}
				finally
				{
					await _idempotencyLock.ReleaseAsync(lockKey, lockValue, CancellationToken.None)
						.ConfigureAwait(false);
				}
			}

			return await GetOrCreateCacheEntryWithoutLockAsync(
					cacheKey,
					requestFingerprint,
					endpoint,
					cts.Token)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogWarning("Cache operation timed out for key {CacheKey}: {Message}", cacheKey, ex.Message);
			throw new TimeoutException($"Cache operation timed out for key: {cacheKey}", ex);
		}
		catch (Exception ex) when (ex is not TimeoutException and not InvalidOperationException)
		{
			_logger.LogError(ex, "Cache operation failed for key {CacheKey}: {Message}", cacheKey, ex.Message);
			throw new InvalidOperationException($"Cache operation failed for key: {cacheKey}", ex);
		}
	}

	private async Task<(bool, IdempotencyCacheEntry?)> GetOrCreateCacheEntryWithoutLockAsync(
		string cacheKey,
		string? requestFingerprint,
		IdempotencyEndpointSettings endpoint,
		CancellationToken cancellationToken)
	{
		var processingTtl = endpoint.ProcessingTtl ?? _options.ProcessingTtl;
		var cacheData = await _distributedCache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
		if (cacheData != null)
		{
			var existing = JsonConvert.DeserializeObject<IdempotencyCacheEntry>(Encoding.UTF8.GetString(cacheData));
			if (existing is not null
				&& existing.Status == "Processing"
				&& IsProcessingExpired(existing))
			{
				await _distributedCache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
			}
			else if (existing is not null)
			{
				return (false, existing);
			}
		}

		var expiresAt = DateTimeOffset.UtcNow.Add(processingTtl);
		var cacheEntry = new IdempotencyCacheEntry
		{
			Status = "Processing",
			Response = null,
			RequestFingerprint = requestFingerprint,
			ProcessingExpiresAtUtc = expiresAt
		};
		var cacheEntryData = JsonConvert.SerializeObject(cacheEntry);

		await _distributedCache.SetAsync(
			cacheKey,
			Encoding.UTF8.GetBytes(cacheEntryData),
			new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = processingTtl },
			cancellationToken).ConfigureAwait(false);

		return (true, cacheEntry);
	}

	private static bool IsProcessingExpired(IdempotencyCacheEntry entry)
	{
		if (entry.ProcessingExpiresAtUtc is { } expires)
			return expires <= DateTimeOffset.UtcNow;

		return false;
	}

	private async Task MarkRequestAsProcessingAsync(
		string cacheKey,
		string? requestFingerprint,
		IdempotencyEndpointSettings endpoint,
		CancellationToken cancellationToken)
	{
		var processingTtl = endpoint.ProcessingTtl ?? _options.ProcessingTtl;
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(_options.CacheOperationTimeout);

		var cacheEntry = new IdempotencyCacheEntry
		{
			Status = "Processing",
			RequestFingerprint = requestFingerprint,
			ProcessingExpiresAtUtc = DateTimeOffset.UtcNow.Add(processingTtl)
		};
		var cacheEntryData = JsonConvert.SerializeObject(cacheEntry);

		await _distributedCache.SetAsync(
			cacheKey,
			Encoding.UTF8.GetBytes(cacheEntryData),
			new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = processingTtl },
			cts.Token).ConfigureAwait(false);
	}
}
