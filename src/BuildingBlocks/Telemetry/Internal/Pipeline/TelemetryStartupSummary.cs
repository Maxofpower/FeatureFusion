using BuildingBlocks.Telemetry.Internal.Exporters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Telemetry.Internal.Pipeline;

/// <summary>
/// Snapshot of the resolved telemetry configuration, logged once at startup.
/// </summary>
/// <remarks>
/// Values are limited to names and modes. OTLP endpoints, OTLP headers, and Azure Monitor
/// connection strings are never captured here.
/// </remarks>
internal sealed class TelemetryStartupSummary
{
    internal const string OtlpEnvironmentExporter = "OTLP (environment)";
    internal const string OtlpExplicitExporter = "OTLP (explicit options)";
    internal const string ConsoleExporter = "Console";
    internal const string AzureMonitorExporter = "Azure Monitor";

    private static readonly Action<ILogger, string, string, string, string, string, Exception?> LogConfigured =
        LoggerMessage.Define<string, string, string, string, string>(
            LogLevel.Information,
            new EventId(1, "TelemetryConfigured"),
            "BuildingBlocks.Telemetry ready for {ServiceName} in {Environment}. Signals: {Signals}. Exporters: {Exporters}. Instrumentation: {Instrumentation}.");

    private static readonly Action<ILogger, string, Exception?> LogNoExporter =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, "TelemetryNoExporter"),
            "BuildingBlocks.Telemetry is collecting telemetry for {ServiceName} but no exporter is configured, so data is discarded. Set OTEL_EXPORTER_OTLP_ENDPOINT, or enable Exporters.Console / Exporters.AzureMonitor.");

    private TelemetryStartupSummary(
        string serviceName,
        string environment,
        IReadOnlyList<string> signals,
        IReadOnlyList<string> exporters,
        IReadOnlyList<string> instrumentation)
    {
        ServiceName = serviceName;
        Environment = environment;
        Signals = signals;
        Exporters = exporters;
        Instrumentation = instrumentation;
    }

    public string ServiceName { get; }

    public string Environment { get; }

    public IReadOnlyList<string> Signals { get; }

    public IReadOnlyList<string> Exporters { get; }

    public IReadOnlyList<string> Instrumentation { get; }

    /// <summary>
    /// <c>true</c> when at least one signal is enabled (otherwise the pipeline is fully off).
    /// </summary>
    public bool HasSignals => Signals.Count > 0;

    /// <summary>
    /// <c>true</c> when telemetry has somewhere to go.
    /// </summary>
    public bool HasExporter => Exporters.Count > 0;

    public static TelemetryStartupSummary Create(
        string serviceName,
        string environment,
        TelemetryOptions options,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        return new TelemetryStartupSummary(
            serviceName,
            string.IsNullOrWhiteSpace(environment) ? "unknown" : environment,
            ResolveSignals(options),
            ResolveExporters(options, configuration),
            ResolveInstrumentation(options));
    }

    /// <summary>
    /// Writes the summary, or a warning when nothing can leave the process.
    /// </summary>
    public void Write(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        LogConfigured(
            logger,
            ServiceName,
            Environment,
            Format(Signals),
            Format(Exporters),
            Format(Instrumentation),
            null);

        if (HasSignals && !HasExporter)
        {
            LogNoExporter(logger, ServiceName, null);
        }
    }

    private static string Format(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values);

    private static IReadOnlyList<string> ResolveSignals(TelemetryOptions options)
    {
        var signals = new List<string>(3);
        if (options.EnableTracing)
        {
            signals.Add("traces");
        }

        if (options.EnableMetrics)
        {
            signals.Add("metrics");
        }

        if (options.EnableLogging)
        {
            signals.Add("logs");
        }

        return signals;
    }

    private static IReadOnlyList<string> ResolveExporters(TelemetryOptions options, IConfiguration configuration)
    {
        var exporters = new List<string>(3);

        if (OtlpExporterRegistration.ShouldUseOtlp(options, configuration))
        {
            exporters.Add(
                OtlpExporterRegistration.CanUseOtlpExporterFastPath(options, configuration)
                    ? OtlpEnvironmentExporter
                    : OtlpExplicitExporter);
        }

        if (options.Exporters.Console.Enabled)
        {
            exporters.Add(ConsoleExporter);
        }

        if (AzureMonitorExporterRegistration.ShouldUseAzureMonitor(options, configuration)
            && !string.IsNullOrWhiteSpace(
                AzureMonitorExporterRegistration.ResolveConnectionString(options, configuration)))
        {
            exporters.Add(AzureMonitorExporter);
        }

        return exporters;
    }

    private static IReadOnlyList<string> ResolveInstrumentation(TelemetryOptions options)
    {
        var instrumentation = options.Instrumentation;
        var enabled = new List<string>(10);

        Add(enabled, instrumentation.AspNetCore, TelemetryComponentTags.AspNetCore);
        Add(enabled, instrumentation.HttpClient, TelemetryComponentTags.HttpClient);
        Add(enabled, instrumentation.Runtime, "runtime");
        Add(enabled, instrumentation.Npgsql, TelemetryComponentTags.Npgsql);
        Add(enabled, instrumentation.SqlClient, TelemetryComponentTags.SqlClient);
        Add(enabled, instrumentation.IncludeFrameworkMeters, "framework-meters");
        Add(enabled, options.IntegrateMediator, TelemetryComponentTags.Mediator);
        Add(enabled, options.IntegrateMcp, TelemetryComponentTags.Mcp);
        Add(enabled, instrumentation.EventBus, TelemetryComponentTags.EventBus);
        Add(enabled, instrumentation.MassTransit, TelemetryComponentTags.MassTransit);

        var customSources = options.Sources.Count(static s => !string.IsNullOrWhiteSpace(s));
        if (customSources > 0)
        {
            enabled.Add($"{customSources} custom source(s)");
        }

        return enabled;
    }

    private static void Add(List<string> target, bool enabled, string name)
    {
        if (enabled)
        {
            target.Add(name);
        }
    }
}
