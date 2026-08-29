namespace BuildingBlocks.Mcp;

/// <summary>
/// Well-known names used by <c>BuildingBlocks.Mcp</c>.
/// </summary>
public static class McpDefaults
{
	/// <summary>
	/// ActivitySource name when <c>UseTelemetry()</c> is enabled.
	/// </summary>
	public const string ActivitySourceName = "BuildingBlocks.Mcp";

	/// <summary>
	/// Default HTTP path mapped by <see cref="Hosting.McpEndpointRouteBuilderExtensions.MapBuildingBlocksMcp"/>.
	/// </summary>
	public const string HttpPath = "/mcp";

	/// <summary>
	/// JSON argument name for write idempotency when <see cref="McpToolAttribute.Idempotent"/> is true.
	/// </summary>
	public const string IdempotencyKeyArgument = "idempotencyKey";

	/// <summary>
	/// JSON Schema <c>format</c> advertised for <see cref="IdempotencyKeyArgument"/> (hint for agents; invoke still accepts any non-empty string).
	/// </summary>
	public const string IdempotencyKeyJsonFormat = "uuid";

	/// <summary>
	/// JSON Schema description for <see cref="IdempotencyKeyArgument"/>.
	/// </summary>
	public const string IdempotencyKeyArgumentDescription =
		"UUID for this write. Generate a new one for a new operation; reuse the same value only when retrying the same request.";

	/// <summary>
	/// JSON argument name for dry-run when <see cref="McpToolAttribute.AllowDryRun"/> is true.
	/// </summary>
	public const string DryRunArgument = "dryRun";

	/// <summary>
	/// JSON argument name for confirmation when <see cref="McpToolAttribute.RequireConfirmation"/> is true.
	/// </summary>
	public const string ConfirmedArgument = "confirmed";

	/// <summary>
	/// MCP resource URI for the enabled tool catalog.
	/// </summary>
	public const string CatalogResourceUri = "catalog://tools";
}
