namespace BuildingBlocks.Telemetry;

/// <summary>
/// Root container for all exporter settings used by <c>AddTelemetry</c>.
/// </summary>
/// <remarks>
/// Prefer env-driven OTLP (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>) for production destinations.
/// Azure Monitor is opt-in and can coexist with OTLP.
/// </remarks>
public sealed class TelemetryExporterOptions
{
    /// <summary>
    /// OTLP exporter (primary production path: SigNoz, collectors, Tempo, etc.).
    /// </summary>
    public TelemetryOtlpExporterOptions Otlp { get; set; } = new();

    /// <summary>
    /// Console exporter for local debugging. When enabled, disables the OTLP <c>UseOtlpExporter()</c> fast-path.
    /// </summary>
    public TelemetryConsoleExporterOptions Console { get; set; } = new();

    /// <summary>
    /// Optional Azure Monitor / Application Insights exporter. Off by default.
    /// Also treated as enabled when <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set.
    /// </summary>
    public TelemetryAzureMonitorExporterOptions AzureMonitor { get; set; } = new();
}
