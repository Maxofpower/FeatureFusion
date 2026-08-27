namespace BuildingBlocks.Telemetry;

/// <summary>
/// Console exporter settings for local debugging of BuildingBlocks.Telemetry.
/// </summary>
/// <remarks>
/// Enabling the console exporter disables the OTLP <c>UseOtlpExporter()</c> fast-path so that
/// Console and OTLP can both be registered via per-signal exporters.
/// </remarks>
public sealed class TelemetryConsoleExporterOptions
{
    /// <summary>
    /// When <c>true</c>, writes traces/metrics/logs to the console. Default: <c>false</c>.
    /// </summary>
    public bool Enabled { get; set; }
}
