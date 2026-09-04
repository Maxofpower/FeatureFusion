namespace BuildingBlocks.Idempotency;

/// <summary>Kind of outcome from <see cref="IdempotencyGate.BeginAsync"/>.</summary>
public enum IdempotencyDecisionKind
{
	/// <summary>First owner — proceed to execute the handler.</summary>
	Execute = 0,

	/// <summary>Replay a stored Completed (or Failed) envelope.</summary>
	Replay = 1,

	/// <summary>Non-expired Processing lease held by another request.</summary>
	ProcessingConflict = 2,

	/// <summary>Same key with a different request fingerprint.</summary>
	FingerprintConflict = 3,

	/// <summary>Completed entry and <see cref="DuplicateCompletedBehavior.Conflict"/>.</summary>
	DuplicateConflict = 4,

	/// <summary>Missing, empty, control-character, over-length, or invalid ULID key.</summary>
	BadKey = 5,

	/// <summary>Caller identity missing and no <see cref="IdempotencyOptions.UserIdFallback"/>.</summary>
	Unauthorized = 6,

	/// <summary>Distributed lock or cache operation failure.</summary>
	LockFailure = 7
}

/// <summary>
/// Gate decision for MVC / Minimal API hosts. Errors map via <see cref="IdempotencyProblemDetails"/>.
/// </summary>
public sealed class IdempotencyDecision
{
	private IdempotencyDecision(
		IdempotencyDecisionKind kind,
		string? cacheKey = null,
		string? requestFingerprint = null,
		IdempotencyEnvelope? envelope = null,
		string? detail = null)
	{
		Kind = kind;
		CacheKey = cacheKey;
		RequestFingerprint = requestFingerprint;
		Envelope = envelope;
		Detail = detail;
	}

	/// <summary>Decision kind.</summary>
	public IdempotencyDecisionKind Kind { get; }

	/// <summary>Composite cache key when known.</summary>
	public string? CacheKey { get; }

	/// <summary>Fingerprint used for this attempt (Execute path).</summary>
	public string? RequestFingerprint { get; }

	/// <summary>Stored envelope when <see cref="Kind"/> is <see cref="IdempotencyDecisionKind.Replay"/>.</summary>
	public IdempotencyEnvelope? Envelope { get; }

	/// <summary>Human-readable detail for ProblemDetails.</summary>
	public string? Detail { get; }

	/// <summary>Whether the host should invoke the action / endpoint.</summary>
	public bool ShouldExecute => Kind == IdempotencyDecisionKind.Execute;

	/// <summary>Creates an Execute decision.</summary>
	public static IdempotencyDecision Execute(string cacheKey, string? requestFingerprint) =>
		new(IdempotencyDecisionKind.Execute, cacheKey, requestFingerprint);

	/// <summary>Creates a Replay decision.</summary>
	public static IdempotencyDecision Replay(string cacheKey, IdempotencyEnvelope envelope) =>
		new(IdempotencyDecisionKind.Replay, cacheKey, envelope: envelope);

	/// <summary>Creates a Processing conflict decision.</summary>
	public static IdempotencyDecision ProcessingConflict(string cacheKey, string detail) =>
		new(IdempotencyDecisionKind.ProcessingConflict, cacheKey, detail: detail);

	/// <summary>Creates a fingerprint conflict decision.</summary>
	public static IdempotencyDecision FingerprintConflict(string cacheKey, string detail) =>
		new(IdempotencyDecisionKind.FingerprintConflict, cacheKey, detail: detail);

	/// <summary>Creates a duplicate Completed conflict decision.</summary>
	public static IdempotencyDecision DuplicateConflict(string cacheKey, string detail) =>
		new(IdempotencyDecisionKind.DuplicateConflict, cacheKey, detail: detail);

	/// <summary>Creates a bad-key decision.</summary>
	public static IdempotencyDecision BadKey(string detail) =>
		new(IdempotencyDecisionKind.BadKey, detail: detail);

	/// <summary>Creates an unauthorized decision.</summary>
	public static IdempotencyDecision Unauthorized(string detail) =>
		new(IdempotencyDecisionKind.Unauthorized, detail: detail);

	/// <summary>Creates a lock/infrastructure failure decision.</summary>
	public static IdempotencyDecision LockFailure(string? cacheKey, string detail) =>
		new(IdempotencyDecisionKind.LockFailure, cacheKey, detail: detail);
}

/// <summary>Cached HTTP response envelope for replay.</summary>
public sealed class IdempotencyEnvelope
{
	/// <summary>Creates an envelope.</summary>
	public IdempotencyEnvelope(int statusCode, string contentType, string body)
	{
		StatusCode = statusCode;
		ContentType = contentType;
		Body = body;
	}

	/// <summary>HTTP status to replay.</summary>
	public int StatusCode { get; }

	/// <summary>Content-Type to replay.</summary>
	public string ContentType { get; }

	/// <summary>Response body text.</summary>
	public string Body { get; }
}

/// <summary>
/// Per-endpoint overrides merged with <see cref="IdempotencyOptions"/>.
/// Null TTL properties mean “use global options”.
/// </summary>
public sealed class IdempotencyEndpointSettings
{
	/// <summary>When true, GetOrCreate is wrapped in <see cref="IIdempotencyLock"/>.</summary>
	public bool UseLock { get; init; }

	/// <summary>Optional Processing TTL override.</summary>
	public TimeSpan? ProcessingTtl { get; init; }

	/// <summary>Optional Completed Entry TTL override.</summary>
	public TimeSpan? EntryTtl { get; init; }

	/// <summary>Creates settings with optional TTL seconds (<c>0</c> = use global).</summary>
	public static IdempotencyEndpointSettings Create(
		bool useLock,
		int processingTtlSeconds = 0,
		int entryTtlSeconds = 0) =>
		new()
		{
			UseLock = useLock,
			ProcessingTtl = processingTtlSeconds > 0 ? TimeSpan.FromSeconds(processingTtlSeconds) : null,
			EntryTtl = entryTtlSeconds > 0 ? TimeSpan.FromSeconds(entryTtlSeconds) : null
		};
}
