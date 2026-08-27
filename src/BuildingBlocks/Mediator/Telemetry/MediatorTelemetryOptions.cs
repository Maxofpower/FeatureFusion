namespace BuildingBlocks.Mediator.Telemetry;

/// <summary>
/// Options for optional Send enrichment via <c>UseTelemetry</c>.
/// Activity wraps the full pipeline + handler (not a pipeline behavior). Metrics stay host-owned.
/// </summary>
public sealed class MediatorTelemetryOptions
{
	/// <summary>
	/// <see cref="System.Diagnostics.ActivitySource"/> name. Host OpenTelemetry must
	/// <c>AddSource</c> this value (default <c>BuildingBlocks.Mediator</c>). Optional override only.
	/// </summary>
	public string ActivitySourceName { get; set; } = "BuildingBlocks.Mediator";

	/// <summary>When true, faults set Activity error status and record the exception.</summary>
	public bool RecordException { get; set; } = true;

	/// <summary>When true, logs start/end (and errors) via <see cref="Microsoft.Extensions.Logging.ILogger"/>.</summary>
	public bool EnableLogging { get; set; } = true;
}
