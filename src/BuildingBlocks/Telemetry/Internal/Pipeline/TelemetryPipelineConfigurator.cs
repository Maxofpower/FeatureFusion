using BuildingBlocks.Telemetry.Internal.Exporters;
using BuildingBlocks.Telemetry.Internal.Instrumentations;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Pipeline;

/// <summary>
/// Orchestrates OpenTelemetry pipeline configuration from a <see cref="TelemetryOptions"/> snapshot.
/// Delegates exporters, resource, and instrumentations to focused internal types.
/// </summary>
internal static class TelemetryPipelineConfigurator
{
    /// <summary>
    /// Configures logging exporters (per-signal OTLP and Console).
    /// </summary>
    public static void ConfigureLoggingExporters(
        OpenTelemetryLoggerOptions logging,
        TelemetryOptions options,
        bool registerPerSignalOtlp)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(options);

        logging.ParseStateValues = true;

        if (registerPerSignalOtlp)
        {
            logging.AddOtlpExporter(o => OtlpExporterRegistration.ApplyOtlpOptions(o, options));
        }

        ConsoleExporterRegistration.ApplyLogging(logging, options);
    }

    /// <summary>
    /// Configures metrics: exemplars, instrumentations, then exporters.
    /// </summary>
    public static void ConfigureMetrics(
        MeterProviderBuilder metrics,
        TelemetryOptions options,
        bool useOtlp,
        bool registerOtlpExporter)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(options);

        if (options.EnableTraceBasedExemplars)
        {
            metrics.SetExemplarFilter(ExemplarFilterType.TraceBased);
        }

        MetricsInstrumentations.Apply(metrics, options);

        if (registerOtlpExporter && useOtlp)
        {
            metrics.AddOtlpExporter(o => OtlpExporterRegistration.ApplyOtlpOptions(o, options));
        }

        ConsoleExporterRegistration.ApplyMetrics(metrics, options);
    }

    /// <summary>
    /// Configures tracing: sources, sampler, instrumentations, then exporters.
    /// </summary>
    public static void ConfigureTracing(
        TracerProviderBuilder tracing,
        string serviceName,
        TelemetryOptions options,
        IHostEnvironment environment,
        bool useOtlp,
        bool registerOtlpExporter)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        tracing.AddSource(serviceName);

        if (options.IntegrateMediator)
        {
            tracing.AddSource(TelemetryDefaults.MediatorActivitySource);
        }

        foreach (var source in options.Sources)
        {
            if (!string.IsNullOrWhiteSpace(source))
            {
                tracing.AddSource(source);
            }
        }

        ApplySampler(tracing, options, environment);

        if (options.SetErrorStatusOnException)
        {
            tracing.SetErrorStatusOnException();
        }

        TracingInstrumentations.Apply(tracing, options);

        if (registerOtlpExporter && useOtlp)
        {
            tracing.AddOtlpExporter(o => OtlpExporterRegistration.ApplyOtlpOptions(o, options));
        }

        ConsoleExporterRegistration.ApplyTracing(tracing, options);
    }

    private static void ApplySampler(
        TracerProviderBuilder tracing,
        TelemetryOptions options,
        IHostEnvironment environment)
    {
        if (options.TracesSamplerRatio is { } ratio)
        {
            var clamped = Math.Clamp(ratio, 0.0, 1.0);
            tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(clamped)));
            return;
        }

        if (options.AlwaysOnSamplerInDevelopment && environment.IsDevelopment())
        {
            tracing.SetSampler(new AlwaysOnSampler());
        }
    }
}
