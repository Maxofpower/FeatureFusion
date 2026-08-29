namespace BuildingBlocks.Mcp;

/// <summary>
/// Stable error codes returned to MCP clients. Prefer these over throwing for domain failures.
/// </summary>
public enum McpErrorCode
{
	/// <summary>JSON or argument validation failed.</summary>
	Validation = 0,

	/// <summary>The requested entity was not found.</summary>
	NotFound = 1,

	/// <summary>The operation conflicts with current state.</summary>
	Conflict = 2,

	/// <summary>The caller is not allowed to use this tool.</summary>
	Forbidden = 3,

	/// <summary>Write tool required <see cref="McpDefaults.IdempotencyKeyArgument"/>.</summary>
	IdempotencyKeyRequired = 4,

	/// <summary>The call was canceled.</summary>
	Canceled = 5,

	/// <summary>The per-tool timeout elapsed.</summary>
	Timeout = 6,

	/// <summary>The rate limiter rejected the call.</summary>
	RateLimited = 7,

	/// <summary>Unhandled handler or infrastructure failure. Stack traces are omitted by default.</summary>
	Internal = 8,

	/// <summary>Destructive tool required <see cref="McpDefaults.ConfirmedArgument"/>.</summary>
	ConfirmationRequired = 9
}
