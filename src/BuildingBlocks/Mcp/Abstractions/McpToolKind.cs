namespace BuildingBlocks.Mcp;

/// <summary>
/// Distinguishes read tools from writes for schema annotations and resilience defaults.
/// </summary>
/// <remarks>
/// Auto-detected from <c>BuildingBlocks.Mediator.IQuery&lt;T&gt;</c> / <c>ICommand</c> when present.
/// Otherwise set <see cref="McpToolAttribute.Kind"/> explicitly.
/// Writes are never retried by this library.
/// </remarks>
public enum McpToolKind
{
	/// <summary>
	/// Kind must be inferred from marker interfaces or set on the attribute.
	/// </summary>
	Unspecified = 0,

	/// <summary>
	/// Read-only tool (queries). Safe to retry at the host if a resilience policy is registered.
	/// </summary>
	Query = 1,

	/// <summary>
	/// Write tool (commands). Not retried by BuildingBlocks.Mcp.
	/// </summary>
	Command = 2
}
