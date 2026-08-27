using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Applies SqlClient tracing and metrics instrumentation when enabled.
/// </summary>
internal static class SqlClientInstrumentation
{
    public static void ApplyTracingIfEnabled(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Instrumentation.SqlClient)
        {
            return;
        }

        tracing.AddSqlClientInstrumentation(o =>
        {
            o.RecordException = options.Instrumentation.RecordException;
            options.Instrumentation.ConfigureSqlClient?.Invoke(o);
            TelemetryComponentEnrichment.ApplySqlClientDefaults(o);
        });
    }

    public static void ApplyMetricsIfEnabled(MeterProviderBuilder metrics, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Instrumentation.SqlClient)
        {
            return;
        }

        metrics.AddSqlClientInstrumentation();
    }
}
