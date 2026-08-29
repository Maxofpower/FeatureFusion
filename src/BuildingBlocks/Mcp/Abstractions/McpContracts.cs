namespace BuildingBlocks.Mcp;

/// <summary>
/// Decides whether a tool appears in <c>list_tools</c> for the current caller.
/// </summary>
public interface IMcpToolFilter
{
	/// <summary>
	/// Returns true when the tool should be advertised and callable.
	/// </summary>
	/// <param name="tool">Catalog descriptor.</param>
	/// <param name="context">Caller context.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	ValueTask<bool> IsVisibleAsync(McpToolDescriptor tool, McpInvokeContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Optional per-tool rate limiter. The default is a no-op.
/// </summary>
public interface IMcpRateLimiter
{
	/// <summary>
	/// Attempts to acquire a permit for the tool.
	/// </summary>
	ValueTask<McpRateLimitDecision> TryAcquireAsync(string toolName, McpInvokeContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Result of a rate-limit check.
/// </summary>
/// <param name="Allowed">True when the call may proceed.</param>
/// <param name="RetryAfterSeconds">Optional retry-after when rejected.</param>
public readonly record struct McpRateLimitDecision(bool Allowed, int? RetryAfterSeconds)
{
	/// <summary>Allows the call.</summary>
	public static McpRateLimitDecision Allow { get; } = new(true, null);

	/// <summary>Rejects the call.</summary>
	public static McpRateLimitDecision Deny(int? retryAfterSeconds = null) => new(false, retryAfterSeconds);
}

/// <summary>
/// Optional idempotency store. When registered, duplicate keys return the cached payload without invoking again.
/// The invoker namespaces keys as <c>toolName + key</c>. <see cref="MemoryIdempotencyStore"/> is single-instance with optional TTL.
/// Multi-instance hosts should register a distributed implementation (for example Redis) of this interface.
/// </summary>
public interface IMcpIdempotencyStore
{
	/// <summary>Gets a previously stored payload JSON, or null. The key is already namespaced by tool name.</summary>
	Task<string?> GetAsync(string key, CancellationToken cancellationToken);

	/// <summary>Stores the success payload JSON for a namespaced key.</summary>
	Task SetAsync(string key, string payloadJson, CancellationToken cancellationToken);
}

/// <summary>
/// Maps a handler return value (including host <c>Result&lt;T&gt;</c>) to <see cref="McpResult{T}"/> boxed as object.
/// </summary>
public interface IMcpResultMapper
{
	/// <summary>Maps a raw handler result.</summary>
	McpResult<object?> Map(object? handlerResult);
}

/// <summary>
/// Optional host resilience wrapper. Core does not retry writes.
/// </summary>
public interface IMcpResiliencePolicy
{
	/// <summary>
	/// Executes the action. Implementations must not retry when <paramref name="kind"/> is <see cref="McpToolKind.Command"/>.
	/// </summary>
	Task<McpResult<object?>> ExecuteAsync(
		Func<CancellationToken, Task<McpResult<object?>>> action,
		McpToolKind kind,
		CancellationToken cancellationToken);
}

/// <summary>
/// Sends a deserialized message to the host (Mediator or a custom dispatcher).
/// </summary>
public interface IMcpMessageDispatcher
{
	/// <summary>
	/// Dispatches <paramref name="message"/> and returns the handler result.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
	Task<object?> SendAsync(object message, CancellationToken cancellationToken);
}

/// <summary>
/// Invokes a named tool from JSON arguments (used by tests and the MCP transport adapters).
/// </summary>
public interface IMcpInvoker
{
	/// <summary>The immutable catalog after filters are applied at list-time separately.</summary>
	IReadOnlyList<McpToolDescriptor> Catalog { get; }

	/// <summary>Tools visible for the current context.</summary>
	Task<IReadOnlyList<McpToolDescriptor>> ListVisibleAsync(McpInvokeContext context, CancellationToken cancellationToken);

	/// <summary>Invokes a tool by name.</summary>
	Task<McpResult<object?>> InvokeAsync(
		string toolName,
		System.Text.Json.JsonElement arguments,
		McpInvokeContext context,
		CancellationToken cancellationToken);
}
