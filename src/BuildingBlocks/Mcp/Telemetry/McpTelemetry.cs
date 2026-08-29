using System.Diagnostics;

namespace BuildingBlocks.Mcp;

/// <summary>
/// Optional ActivitySource around tool invoke. Omit for zero library telemetry overhead.
/// </summary>
public sealed class McpTelemetryOptions
{
	/// <summary>
	/// ActivitySource name. Default: <see cref="McpDefaults.ActivitySourceName"/>.
	/// </summary>
	public string ActivitySourceName { get; set; } = McpDefaults.ActivitySourceName;

	/// <summary>
	/// When true, include exception messages in <see cref="McpErrorCode.Internal"/> (Development only recommended).
	/// </summary>
	public bool IncludeExceptionDetails { get; set; }
}

/// <summary>
/// Holds the ActivitySource when telemetry is enabled.
/// </summary>
public sealed class McpTelemetry
{
	/// <summary>Creates telemetry from options.</summary>
	public McpTelemetry(McpTelemetryOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		Enabled = true;
		Source = new ActivitySource(string.IsNullOrWhiteSpace(options.ActivitySourceName)
			? McpDefaults.ActivitySourceName
			: options.ActivitySourceName);
		IncludeExceptionDetails = options.IncludeExceptionDetails;
	}

	/// <summary>Always true when this instance is registered.</summary>
	public bool Enabled { get; }

	/// <summary>Activity source for mcp.tool spans.</summary>
	public ActivitySource Source { get; }

	/// <summary>Whether internal errors include exception messages.</summary>
	public bool IncludeExceptionDetails { get; }
}
