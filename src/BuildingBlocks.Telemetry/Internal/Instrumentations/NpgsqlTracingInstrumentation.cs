using Npgsql;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Applies Npgsql tracing instrumentation when enabled.
/// </summary>
internal static class NpgsqlTracingInstrumentation
{
    public static void ApplyIfEnabled(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Instrumentation.Npgsql)
        {
            return;
        }

        tracing.AddNpgsql();
    }
}
