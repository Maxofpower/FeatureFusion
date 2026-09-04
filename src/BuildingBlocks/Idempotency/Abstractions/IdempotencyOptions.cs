namespace BuildingBlocks.Idempotency;

/// <summary>
/// Configuration for HTTP idempotency. Hosts that need Lab-compatible anonymous
/// user scoping must set <see cref="UserIdFallback"/> explicitly (for example <c>"123"</c>).
/// </summary>
/// <remarks>
/// <para>
/// TTL policy (publishable expiry): <see cref="ProcessingTtl"/> bounds in-flight leases;
/// <see cref="EntryTtl"/> bounds Completed replays. Keep <see cref="ProcessingTtl"/> longer than
/// the worst-case successful handler, or a second request may run while the first is still in flight
/// after lease expiry (lock covers GetOrCreate only).
/// </para>
/// <para>
/// Replay header default is <c>X-Idempotent-Response</c>. Override via <see cref="CachedResponseHeader"/> if needed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddBuildingBlocksIdempotency(o =&gt;
/// {
///     o.UserIdFallback = "123";
///     o.ProcessingTtl = TimeSpan.FromMinutes(2);
/// });
/// </code>
/// </example>
public sealed class IdempotencyOptions
{
	/// <summary>HTTP header carrying the idempotency key. Default <c>Idempotency-Key</c>.</summary>
	public string HeaderName { get; set; } = "Idempotency-Key";

	/// <summary>Cache key prefix. Default <c>Idempotency</c>.</summary>
	public string KeyPrefix { get; set; } = "Idempotency";

	/// <summary>
	/// Response header set on replay. Default <c>X-Idempotent-Response</c>.
	/// </summary>
	public string CachedResponseHeader { get; set; } = "X-Idempotent-Response";

	/// <summary>TTL for Completed (and Failed read-compat) cache entries. Default 30 minutes.</summary>
	public TimeSpan EntryTtl { get; set; } = TimeSpan.FromMinutes(30);

	/// <summary>
	/// Lifetime of a Processing entry. After this, the same key may run again (abandoned in-flight).
	/// Default 2 minutes. Must be shorter than <see cref="EntryTtl"/> in practice.
	/// </summary>
	public TimeSpan ProcessingTtl { get; set; } = TimeSpan.FromMinutes(2);

	/// <summary>Timeout for GetOrCreate / Mark Processing. Default 10 seconds.</summary>
	public TimeSpan CacheOperationTimeout { get; set; } = TimeSpan.FromSeconds(10);

	/// <summary>Timeout for Complete / Remove. Default 5 seconds.</summary>
	public TimeSpan CacheWriteTimeout { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>Redis lock expiry when <c>UseLock</c> is true. Default 10 seconds.</summary>
	public TimeSpan LockExpiry { get; set; } = TimeSpan.FromSeconds(10);

	/// <summary>Claim type used for the user segment of the cache key.</summary>
	public string UserIdClaimType { get; set; } = System.Security.Claims.ClaimTypes.NameIdentifier;

	/// <summary>
	/// Optional extra claim types inserted into the cache key after the prefix and before the user id
	/// (for example tenant id). Empty by default — key remains <c>{prefix}_{user}_{key}</c>.
	/// </summary>
	public IList<string> KeyScopeClaimTypes { get; } = new List<string>();

	/// <summary>
	/// Optional fallback when the user claim is missing.
	/// Null/empty means missing claim → unauthorized (HTTP 401 ProblemDetails).
	/// </summary>
	public string? UserIdFallback { get; set; }

	/// <summary>When true (default), the header value must parse as a ULID.</summary>
	public bool RequireUlid { get; set; } = true;

	/// <summary>
	/// Maximum allowed length of the raw header value when validated.
	/// Default 256. Applied even when <see cref="RequireUlid"/> is false.
	/// </summary>
	public int MaxKeyLength { get; set; } = 256;

	/// <summary>
	/// When true, SHA-256 of <c>method + "\n" + path + "\n" + body</c> is stored on first miss and compared on reuse.
	/// Default false (same key always replays regardless of body).
	/// </summary>
	public bool EnableRequestFingerprint { get; set; }

	/// <summary>
	/// HTTP status when the same key is reused with a different fingerprint.
	/// Default 422 Unprocessable Entity.
	/// </summary>
	public int FingerprintConflictStatusCode { get; set; } = 422;

	/// <summary>
	/// HTTP status when another request holds a non-expired Processing lease.
	/// Default 409 Conflict.
	/// </summary>
	public int ProcessingConflictStatusCode { get; set; } = 409;

	/// <summary>
	/// Behavior when the same key hits a Completed (or Failed) entry.
	/// Default <see cref="DuplicateCompletedBehavior.Replay"/>.
	/// </summary>
	public DuplicateCompletedBehavior DuplicateCompletedBehavior { get; set; } = DuplicateCompletedBehavior.Replay;

	/// <summary>
	/// HTTP status when <see cref="DuplicateCompletedBehavior"/> is <see cref="DuplicateCompletedBehavior.Conflict"/>.
	/// Default 409 Conflict.
	/// </summary>
	public int DuplicateConflictStatusCode { get; set; } = 409;
}
