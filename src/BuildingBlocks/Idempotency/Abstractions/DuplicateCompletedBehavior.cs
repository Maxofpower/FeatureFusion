namespace BuildingBlocks.Idempotency;

/// <summary>
/// Strategy when the same <c>Idempotency-Key</c> is reused after a Completed (or Failed) cache entry.
/// </summary>
public enum DuplicateCompletedBehavior
{
	/// <summary>
	/// Replay the stored status, content-type, and body (default; Exp 3 compatible).
	/// </summary>
	Replay = 0,

	/// <summary>
	/// Reject the reuse with <see cref="IdempotencyOptions.DuplicateConflictStatusCode"/> (default 409).
	/// </summary>
	Conflict = 1
}
