namespace BuildingBlocks.Mediator.Telemetry;

/// <summary>
/// Options for optional Send enrichment via <c>UseTelemetry</c>.
/// Activity wraps the full pipeline + handler (not a pipeline behavior).
/// When <see cref="EnableMetrics"/> is true, a <c>Meter</c> records Send duration and counts.
/// Omit <c>UseTelemetry</c> for zero telemetry overhead.
/// </summary>
public sealed class MediatorTelemetryOptions
{
	/// <summary>
	/// <see cref="System.Diagnostics.ActivitySource"/> name. Host OpenTelemetry must
	/// <c>AddSource</c> this value (default <c>BuildingBlocks.Mediator</c>). Optional override only.
	/// </summary>
	public string ActivitySourceName { get; set; } = "BuildingBlocks.Mediator";

	/// <summary>
	/// <see cref="System.Diagnostics.Metrics.Meter"/> name. Host OpenTelemetry must
	/// <c>AddMeter</c> this value. When unset, <c>UseTelemetry</c> copies <see cref="ActivitySourceName"/>.
	/// </summary>
	public string MeterName { get; set; } = "";

	/// <summary>When true, faults set Activity error status and record the exception.</summary>
	public bool RecordException { get; set; } = true;

	/// <summary>When true, logs start/end (and errors) via <see cref="Microsoft.Extensions.Logging.ILogger"/>.</summary>
	public bool EnableLogging { get; set; } = true;

	/// <summary>
	/// When true (default), records <c>mediator.send.duration</c> (histogram, ms) and
	/// <c>mediator.send</c> (counter) on the meter named <see cref="MeterName"/>.
	/// </summary>
	public bool EnableMetrics { get; set; } = true;
}
