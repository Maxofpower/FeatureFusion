namespace BuildingBlocks.Idempotency;

/// <summary>Well-known <c>idempotency.outcome</c> Activity tag values.</summary>
public static class IdempotencyOutcomes
{
	/// <summary>First successful execution cached as Completed.</summary>
	public const string Executed = "executed";

	/// <summary>Cached Completed (or Failed) response replayed.</summary>
	public const string Replayed = "replayed";

	/// <summary>In-flight Processing conflict.</summary>
	public const string ProcessingConflict = "processing_conflict";

	/// <summary>Same key, different request fingerprint.</summary>
	public const string FingerprintConflict = "fingerprint_conflict";

	/// <summary>Completed entry rejected under Conflict strategy.</summary>
	public const string DuplicateConflict = "duplicate_conflict";

	/// <summary>Missing or invalid Idempotency-Key.</summary>
	public const string BadKey = "bad_key";

	/// <summary>Caller identity missing.</summary>
	public const string Unauthorized = "unauthorized";

	/// <summary>Distributed lock not acquired or cache operation timed out.</summary>
	public const string LockFailure = "lock_failure";
}
