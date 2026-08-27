using System.Diagnostics;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Instrumentation.SqlClient;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Applies default <see cref="TelemetryComponentTags.TagName"/> enrichment on instrumentation Enrich hooks.
/// User <c>Configure*</c> callbacks run first; defaults are chained afterward so custom enrichers still run.
/// </summary>
internal static class TelemetryComponentEnrichment
{
    public static void SetComponent(Activity activity, string component) =>
        activity.SetTag(TelemetryComponentTags.TagName, component);

    public static void ApplyAspNetCoreDefaults(AspNetCoreTraceInstrumentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var component = TelemetryComponentTags.AspNetCore;
        options.EnrichWithHttpRequest = Chain(options.EnrichWithHttpRequest, component);
        options.EnrichWithHttpResponse = Chain(options.EnrichWithHttpResponse, component);
        options.EnrichWithException = Chain(options.EnrichWithException, component);
    }

    public static void ApplyHttpClientDefaults(HttpClientTraceInstrumentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var component = TelemetryComponentTags.HttpClient;
        options.EnrichWithHttpRequestMessage = Chain(options.EnrichWithHttpRequestMessage, component);
        options.EnrichWithHttpResponseMessage = Chain(options.EnrichWithHttpResponseMessage, component);
        options.EnrichWithHttpWebRequest = Chain(options.EnrichWithHttpWebRequest, component);
        options.EnrichWithHttpWebResponse = Chain(options.EnrichWithHttpWebResponse, component);
        options.EnrichWithException = Chain(options.EnrichWithException, component);
    }

    public static void ApplySqlClientDefaults(SqlClientTraceInstrumentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
#if !NETFRAMEWORK
        options.EnrichWithSqlCommand = Chain(
            options.EnrichWithSqlCommand,
            TelemetryComponentTags.SqlClient);
#endif
    }

    internal static Action<Activity, TContext> Chain<TContext>(
        Action<Activity, TContext>? existing,
        string component) =>
        (activity, context) =>
        {
            SetComponent(activity, component);
            existing?.Invoke(activity, context);
        };

    internal static Action<Activity, Exception> Chain(
        Action<Activity, Exception>? existing,
        string component) =>
        (activity, exception) =>
        {
            SetComponent(activity, component);
            existing?.Invoke(activity, exception);
        };
}
