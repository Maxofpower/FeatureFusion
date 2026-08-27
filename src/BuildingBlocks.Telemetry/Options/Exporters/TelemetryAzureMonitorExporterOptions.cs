namespace BuildingBlocks.Telemetry;

/// <summary>
/// Azure Monitor / Application Insights exporter (per-signal; can coexist with OTLP).
/// </summary>
/// <remarks>
/// Treated as enabled when <see cref="Enabled"/> is <c>true</c>, <see cref="ConnectionString"/> is set,
/// or <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is present (Aspire ServiceDefaults pattern).
/// </remarks>
public sealed class TelemetryAzureMonitorExporterOptions
{
    /// <summary>
    /// When <c>true</c>, registers Azure Monitor exporters. Default: <c>false</c>.
    /// Also treated as enabled when a connection string is available via options or environment.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Application Insights connection string. Optional when <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set.
    /// </summary>
    public string? ConnectionString { get; set; }
}
