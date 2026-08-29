namespace BuildingBlocks.Mcp;

/// <summary>
/// Marks a concrete message type <strong>or a public static endpoint method</strong> as an opt-in MCP tool.
/// Types/methods without this attribute are never exposed.
/// </summary>
/// <remarks>
/// On a command/query/DTO: catalog deserializes JSON into that type and dispatches (<c>UseDispatcher</c>).
/// On a public static Minimal API method: JSON binds to the request parameter; other parameters resolve from DI.
/// MVC controllers are unsupported for now (actions, <c>[FromHeader]</c>, <c>ActionResult</c>).
/// Use a public static Minimal API method or a message type. Do not use for OpenAPI generation.
/// Catalog: docs/linkedin-posts.md (<c>mcp-message-tools</c>).
/// </remarks>
/// <example>
/// <code>
/// [McpTool("orders.create", Description = "Create an order", Kind = McpToolKind.Command, Idempotent = true)]
/// public sealed record CreateOrder(int ProductId, int Quantity);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class McpToolAttribute : Attribute
{
	/// <summary>
	/// Initializes the attribute with the MCP tool name (for example <c>orders.create</c>).
	/// </summary>
	/// <param name="name">Unique tool name advertised to clients.</param>
	/// <exception cref="ArgumentException"><paramref name="name"/> is null or whitespace.</exception>
	public McpToolAttribute(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Tool name is required.", nameof(name));
		Name = name.Trim();
	}

	/// <summary>
	/// Unique tool name advertised to MCP clients.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Human-readable description. Required at catalog build (fail-fast if missing).
	/// </summary>
	public string Description { get; set; } = string.Empty;

	/// <summary>
	/// Query vs command. Leave <see cref="McpToolKind.Unspecified"/> to infer from Mediator markers.
	/// </summary>
	public McpToolKind Kind { get; set; }

	/// <summary>
	/// When true on a <see cref="McpToolKind.Command"/>, the client must send <see cref="McpDefaults.IdempotencyKeyArgument"/>.
	/// Defaults to true. Queries never use the idempotency store (GET-style). Set false to opt a command out.
	/// </summary>
	public bool Idempotent { get; set; } = true;

	/// <summary>
	/// When true, the client may send <see cref="McpDefaults.DryRunArgument"/>.
	/// </summary>
	public bool AllowDryRun { get; set; }

	/// <summary>
	/// When true, the client must send <see cref="McpDefaults.ConfirmedArgument"/> as true.
	/// </summary>
	public bool RequireConfirmation { get; set; }

	/// <summary>
	/// Optional invoke timeout in milliseconds. Zero or negative means no extra timeout.
	/// </summary>
	public int TimeoutMilliseconds { get; set; }

	/// <summary>
	/// Optional feature-flag name. When set, <see cref="IMcpToolFilter"/> implementations can hide the tool.
	/// </summary>
	public string? FeatureFlag { get; set; }

	/// <summary>
	/// When non-empty, the user must be in at least one of these roles (HTTP transport).
	/// </summary>
	public string[] Roles { get; set; } = [];
}
