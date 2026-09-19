namespace BuildingBlocks.Mcp;

/// <summary>
/// Options for distributed MCP write idempotency (wait-and-replay).
/// The lease is the in-flight safety window, not an exactly-once guarantee. There is no lease renewal.
/// </summary>
public sealed class McpIdempotencyOptions
{
	/// <summary>
	/// How long a lock holder may run <c>InvokeCore</c> before another instance may acquire.
	/// Default 2 minutes. Keep this longer than the worst-case successful handler.
	/// </summary>
	public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);

	/// <summary>
	/// TTL for completed success payloads. <see langword="null"/> means the cache implementation's default (no absolute expiry from this package).
	/// </summary>
	public TimeSpan? PayloadTtl { get; set; } = TimeSpan.FromHours(1);

	/// <summary>
	/// How long a waiter polls <c>Get</c> / retries acquire before <see cref="McpErrorCode.Conflict"/>.
	/// Waiters replay on a completed payload; they do not return HTTP Processing/409 while the lease is valid.
	/// </summary>
	public TimeSpan AcquireWaitBudget { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>Delay between waiter poll attempts.</summary>
	public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(20);
}
