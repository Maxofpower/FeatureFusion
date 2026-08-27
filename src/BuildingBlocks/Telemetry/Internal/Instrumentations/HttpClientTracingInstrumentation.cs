using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Applies HttpClient tracing instrumentation when enabled.
/// </summary>
internal static class HttpClientTracingInstrumentation
{
    public static void ApplyIfEnabled(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Instrumentation.HttpClient)
        {
            return;
        }

        tracing.AddHttpClientInstrumentation(o =>
        {
            o.RecordException = options.Instrumentation.RecordException;
            options.Instrumentation.ConfigureHttpClient?.Invoke(o);
            TelemetryComponentEnrichment.ApplyHttpClientDefaults(o);
        });
    }
}
