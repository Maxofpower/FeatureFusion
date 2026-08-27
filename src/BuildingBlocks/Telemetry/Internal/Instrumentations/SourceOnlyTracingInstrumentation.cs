using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Registers source-only ActivitySources (MassTransit, EventBus) when enabled.
/// </summary>
internal static class SourceOnlyTracingInstrumentation
{
    public static void ApplyIfEnabled(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Instrumentation.MassTransit)
        {
            tracing.AddSource(TelemetryDefaults.MassTransitActivitySource);
        }

        if (options.Instrumentation.EventBus)
        {
            tracing.AddSource(TelemetryDefaults.EventBusActivitySource);
        }
    }
}
