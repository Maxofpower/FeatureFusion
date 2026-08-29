namespace BuildingBlocks.Telemetry;

/// <summary>
/// Root options for <c>BuildingBlocks.Telemetry</c>, bound from the <c>Telemetry</c> configuration section.
/// </summary>
/// <remarks>
/// Prefer env-driven OTLP (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>) for destinations so the same binary
/// works in Aspire local-dev, CI, and production collectors without code changes.
/// </remarks>
public sealed class TelemetryOptions
{
    /// <summary>
    /// Configuration section name (<c>Telemetry</c>).
    /// </summary>
    public const string SectionName = "Telemetry";

    /// <summary>
    /// Service name for the OpenTelemetry resource (<c>service.name</c>).
    /// Defaults to <c>IHostEnvironment.ApplicationName</c> when empty.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Optional service namespace attribute (<c>service.namespace</c>).
    /// </summary>
    public string? ServiceNamespace { get; set; }

    /// <summary>
    /// Optional service version attribute (<c>service.version</c>).
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Additional resource attributes (key/value) merged onto the OpenTelemetry resource.
    /// <c>deployment.environment</c> / <c>service.environment</c> are always set from the host environment.
    /// </summary>
    public Dictionary<string, string> ResourceAttributes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Enables tracing. Default: <c>true</c>.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Enables metrics. Default: <c>true</c>.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enables OpenTelemetry logging bridge. Default: <c>true</c>.
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Instrumentation feature flags and advanced <c>Configure*</c> callbacks
    /// (AspNetCore, HttpClient, SqlClient, source-only MassTransit/EventBus).
    /// </summary>
    public TelemetryInstrumentationOptions Instrumentation { get; set; } = new();

    /// <summary>
    /// Exporter configuration (OTLP, Console, optional Azure Monitor).
    /// Prefer <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> for production destinations.
    /// </summary>
    public TelemetryExporterOptions Exporters { get; set; } = new();

    /// <summary>
    /// Extra ActivitySource names to register with the tracer.
    /// </summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>
    /// Extra Meter names to register with the meter provider.
    /// </summary>
    public List<string> Meters { get; set; } = [];

    /// <summary>
    /// When <c>true</c>, automatically adds <see cref="TelemetryDefaults.MediatorActivitySource"/>
    /// to tracing and <see cref="TelemetryDefaults.MediatorMeter"/> to metrics.
    /// Default: <c>true</c>.
    /// </summary>
    public bool IntegrateMediator { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, adds <see cref="TelemetryDefaults.McpActivitySource"/> so MCP tool spans export.
    /// Default: <c>false</c> (opt-in).
    /// </summary>
    public bool IntegrateMcp { get; set; }

    /// <summary>
    /// Optional ratio-based sampler (0.0–1.0). Wrapped in <c>ParentBasedSampler</c>.
    /// When null: AlwaysOn in Development, SDK default otherwise.
    /// Set explicitly to force a ratio in every environment.
    /// </summary>
    public double? TracesSamplerRatio { get; set; }

    /// <summary>
    /// When <c>true</c> (default), force <c>AlwaysOnSampler</c> in the Development environment
    /// unless <see cref="TracesSamplerRatio"/> is set.
    /// </summary>
    public bool AlwaysOnSamplerInDevelopment { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), mark span status Error on unhandled exceptions.
    /// </summary>
    public bool SetErrorStatusOnException { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), enable trace-based metric exemplars.
    /// </summary>
    public bool EnableTraceBasedExemplars { get; set; } = true;
}
