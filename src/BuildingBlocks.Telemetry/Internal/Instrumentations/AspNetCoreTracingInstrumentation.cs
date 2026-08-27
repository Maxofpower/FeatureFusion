using Microsoft.AspNetCore.Http;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Applies ASP.NET Core tracing instrumentation when enabled.
/// </summary>
internal static class AspNetCoreTracingInstrumentation
{
    public static void ApplyIfEnabled(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(tracing);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Instrumentation.AspNetCore)
        {
            return;
        }

        tracing.AddAspNetCoreInstrumentation(o =>
        {
            o.RecordException = options.Instrumentation.RecordException;
            if (options.Instrumentation.FilterHealthCheckRequests)
            {
                o.Filter = httpContext => !IsExcludedPath(httpContext.Request.Path, options);
            }

            options.Instrumentation.ConfigureAspNetCore?.Invoke(o);
            TelemetryComponentEnrichment.ApplyAspNetCoreDefaults(o);
        });
    }

    private static bool IsExcludedPath(PathString path, TelemetryOptions options)
    {
        var prefixes = options.Instrumentation.ExcludedPathPrefixes;
        if (prefixes is null || prefixes.Count == 0)
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix) && path.StartsWithSegments(prefix))
            {
                return true;
            }
        }

        return false;
    }
}
