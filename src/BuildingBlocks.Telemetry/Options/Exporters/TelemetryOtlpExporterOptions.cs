namespace BuildingBlocks.Telemetry;

/// <summary>
/// OTLP exporter settings for BuildingBlocks.Telemetry.
/// </summary>
/// <remarks>
/// When only <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (and Console / explicit Endpoint/Headers are off),
/// the host uses <c>UseOtlpExporter()</c> for traces, metrics, and logs. Explicit
/// <see cref="Endpoint"/> or <see cref="Headers"/> forces per-signal <c>AddOtlpExporter</c> instead —
/// those two registration styles must not be mixed.
/// </remarks>
public sealed class TelemetryOtlpExporterOptions
{
    /// <summary>
    /// When <c>true</c>, registers the OTLP exporter. Also enabled automatically when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Explicit OTLP endpoint (absolute URI, e.g. <c>http://localhost:4317</c>).
    /// Optional when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set. Setting this forces the per-signal
    /// exporter path (cannot mix with <c>UseOtlpExporter()</c> fast-path).
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// OTLP protocol. Default: <see cref="TelemetryOtlpProtocol.Grpc"/>.
    /// Prefer this over string configuration when setting options in code.
    /// </summary>
    /// <remarks>
    /// Applied on the per-signal <c>AddOtlpExporter</c> path only. The <c>UseOtlpExporter()</c>
    /// fast-path (env-driven OTLP with no explicit Endpoint/Headers/Console) ignores this value
    /// and uses <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> instead.
    /// </remarks>
    public TelemetryOtlpProtocol Protocol { get; set; } = TelemetryOtlpProtocol.Grpc;

    /// <summary>
    /// Optional string protocol for <c>appsettings</c> binding (<c>grpc</c>, <c>http/protobuf</c>).
    /// When non-empty, takes precedence over <see cref="Protocol"/>.
    /// </summary>
    public string? ProtocolName { get; set; }

    /// <summary>
    /// Optional headers (e.g. ingestion keys) as <c>key=value</c> pairs joined by commas.
    /// When set, forces the per-signal exporter path.
    /// </summary>
    public string? Headers { get; set; }
}
