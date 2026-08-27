using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Exporters;

/// <summary>
/// Registers the console exporter on logging, metrics, and tracing builders when enabled.
/// </summary>
internal static class ConsoleExporterRegistration
{
    public static void ApplyLogging(OpenTelemetryLoggerOptions logging, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Exporters.Console.Enabled)
        {
            logging.AddConsoleExporter();
        }
    }

    public static void ApplyMetrics(MeterProviderBuilder metrics, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Exporters.Console.Enabled)
        {
            metrics.AddConsoleExporter();
        }
    }

    public static void ApplyTracing(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Exporters.Console.Enabled)
        {
            tracing.AddConsoleExporter();
        }
    }
}
