using OpenTelemetry.Metrics;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Dispatches metrics instrumentations from <see cref="TelemetryInstrumentationOptions"/>.
/// </summary>
internal static class MetricsInstrumentations
{
    public static void Apply(MeterProviderBuilder metrics, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Instrumentation.AspNetCore)
        {
            metrics.AddAspNetCoreInstrumentation();
        }

        if (options.Instrumentation.HttpClient)
        {
            metrics.AddHttpClientInstrumentation();
        }

        if (options.Instrumentation.Runtime)
        {
            metrics.AddRuntimeInstrumentation();
        }

        if (options.Instrumentation.IncludeFrameworkMeters)
        {
            metrics.AddMeter("Microsoft.AspNetCore.Hosting");
            metrics.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
            metrics.AddMeter("Microsoft.AspNetCore.Routing");
            metrics.AddMeter("Microsoft.AspNetCore.Diagnostics");
            metrics.AddMeter("Microsoft.AspNetCore.Authentication");
            metrics.AddMeter("Microsoft.AspNetCore.Authorization");
            metrics.AddMeter("Microsoft.AspNetCore.MemoryPool");
            metrics.AddMeter("System.Net.Http");
            metrics.AddMeter("System.Net.NameResolution");
        }

        SqlClientInstrumentation.ApplyMetricsIfEnabled(metrics, options);

        if (options.IntegrateMediator)
        {
            metrics.AddMeter(TelemetryDefaults.MediatorMeter);
        }

        foreach (var meter in options.Meters)
        {
            if (!string.IsNullOrWhiteSpace(meter))
            {
                metrics.AddMeter(meter);
            }
        }
    }
}
