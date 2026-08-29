namespace BuildingBlocks.Mcp;

/// <summary>
/// Immutable description of one catalogued tool.
/// </summary>
public sealed record McpToolDescriptor
{
	/// <summary>
	/// MCP tool name.
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// Description shown to models.
	/// </summary>
	public required string Description { get; init; }

	/// <summary>
	/// CLR message type deserialized from tool arguments.
	/// </summary>
	public required Type MessageType { get; init; }

	/// <summary>
	/// Query vs command.
	/// </summary>
	public McpToolKind Kind { get; init; }

	/// <summary>
	/// Whether an idempotency key is required.
	/// </summary>
	public bool Idempotent { get; init; }

	/// <summary>
	/// Whether dry-run is accepted.
	/// </summary>
	public bool AllowDryRun { get; init; }

	/// <summary>
	/// Whether confirmation is required.
	/// </summary>
	public bool RequireConfirmation { get; init; }

	/// <summary>
	/// Optional timeout.
	/// </summary>
	public TimeSpan? Timeout { get; init; }

	/// <summary>
	/// Optional feature flag name.
	/// </summary>
	public string? FeatureFlag { get; init; }

	/// <summary>
	/// Role names required on HTTP (any-of). Empty means no role check in the built-in filter.
	/// </summary>
	public IReadOnlyList<string> Roles { get; init; } = [];

	/// <summary>
	/// Public properties included in the JSON schema (excludes protocol args).
	/// </summary>
	public IReadOnlyList<McpJsonProperty> Properties { get; init; } = [];

	internal Func<IServiceProvider, object, McpInvokeContext, CancellationToken, Task<McpResult<object?>>>? Handler { get; init; }
}

/// <summary>
/// One JSON Schema property derived from a CLR public property.
/// </summary>
/// <param name="Name">JSON name.</param>
/// <param name="JsonType">JSON Schema type keyword.</param>
/// <param name="Required">Whether clients must send the property.</param>
/// <param name="Description">Optional description.</param>
public sealed record McpJsonProperty(string Name, string JsonType, bool Required, string? Description)
{
	/// <summary>
	/// String enum member names when the CLR type is a string-backed enum.
	/// </summary>
	public IReadOnlyList<string>? EnumNames { get; init; }

	/// <summary>
	/// Numeric enum values when the CLR type is an integer-backed enum.
	/// </summary>
	public IReadOnlyList<long>? EnumValues { get; init; }
}
