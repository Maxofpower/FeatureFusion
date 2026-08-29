using System.Security.Claims;

namespace BuildingBlocks.Mcp;

/// <summary>
/// Per-call context passed to filters, rate limiters, and handlers.
/// </summary>
/// <param name="User">Authenticated user on HTTP transport; null for stdio unless the host sets it.</param>
/// <param name="IdempotencyKey">Idempotency key for write tools.</param>
/// <param name="DryRun">True when the client requested a dry-run.</param>
/// <param name="Confirmed">True when the client confirmed a destructive tool.</param>
public sealed record McpInvokeContext(
	ClaimsPrincipal? User,
	string? IdempotencyKey,
	bool DryRun,
	bool Confirmed)
{
	/// <summary>
	/// Empty context for tests and stdio with no identity.
	/// </summary>
	public static McpInvokeContext None { get; } = new(null, null, DryRun: false, Confirmed: false);
}
