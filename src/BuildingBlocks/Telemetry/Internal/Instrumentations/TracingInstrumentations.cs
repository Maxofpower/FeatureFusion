using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Dispatches tracing instrumentations from <see cref="TelemetryInstrumentationOptions"/>.
/// </summary>
internal static class TracingInstrumentations
{
    public static void Apply(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);

        SourceOnlyTracingInstrumentation.ApplyIfEnabled(tracing, options);
        AspNetCoreTracingInstrumentation.ApplyIfEnabled(tracing, options);
        HttpClientTracingInstrumentation.ApplyIfEnabled(tracing, options);
        NpgsqlTracingInstrumentation.ApplyIfEnabled(tracing, options);
        SqlClientInstrumentation.ApplyTracingIfEnabled(tracing, options);
        tracing.AddProcessor(new TelemetryComponentActivityProcessor());
    }
}
