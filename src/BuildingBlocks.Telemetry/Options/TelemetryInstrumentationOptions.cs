using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Instrumentation.SqlClient;

namespace BuildingBlocks.Telemetry;

/// <summary>
/// Instrumentation feature flags and advanced configure callbacks for BuildingBlocks.Telemetry.
/// </summary>
/// <remarks>
/// <para>
/// Stable flags default <c>true</c>; SqlClient, MassTransit, and EventBus default <c>false</c>.
/// EF Core, Redis, gRPC client, and Prometheus scrape are not first-class in this package —
/// add the contrib package and <see cref="TelemetryBuilder.ConfigureTracing"/> if you need them.
/// </para>
/// <para>
/// Use <c>Configure*</c> callbacks for Enrich / Filter / tags (via <c>activity.SetTag</c>) on the
/// underlying OpenTelemetry instrumentation options. Order:
/// Filter / <see cref="RecordException"/> defaults → user callback →
/// <see cref="TelemetryComponentTags.TagName"/> enrichment.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// builder.AddTelemetry(o =>
/// {
///     o.Instrumentation.SqlClient = true;
///     o.Instrumentation.ConfigureSqlClient = opts =>
///     {
///         opts.EnrichWithSqlCommand = (activity, command) =>
///             activity.SetTag("db.command_type", command.CommandType.ToString());
///     };
/// });
/// </code>
/// </example>
public sealed class TelemetryInstrumentationOptions
{
    /// <summary>
    /// Default ASP.NET Core path prefixes excluded from tracing when
    /// <see cref="FilterHealthCheckRequests"/> is enabled.
    /// </summary>
    public static IReadOnlyList<string> DefaultExcludedPathPrefixes { get; } =
        Array.AsReadOnly(["/health", "/alive", "/ready", "/metrics"]);

    /// <summary>
    /// Creates a mutable copy of <see cref="DefaultExcludedPathPrefixes"/>.
    /// </summary>
    public static List<string> CreateDefaultExcludedPathPrefixes() =>
        [.. DefaultExcludedPathPrefixes];

    /// <summary>
    /// ASP.NET Core request instrumentation (traces + metrics). Default: <c>true</c>.
    /// </summary>
    public bool AspNetCore { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), ASP.NET Core tracing skips paths in
    /// <see cref="ExcludedPathPrefixes"/> (Aspire ServiceDefaults convention).
    /// Applied before <see cref="ConfigureAspNetCore"/>; the callback may replace <c>Filter</c>.
    /// </summary>
    public bool FilterHealthCheckRequests { get; set; } = true;

    /// <summary>
    /// Path prefixes excluded from ASP.NET Core tracing when
    /// <see cref="FilterHealthCheckRequests"/> is enabled.
    /// Default: <c>/health</c>, <c>/alive</c>, <c>/ready</c>, <c>/metrics</c>.
    /// </summary>
    public List<string> ExcludedPathPrefixes { get; set; } = CreateDefaultExcludedPathPrefixes();

    /// <summary>
    /// When <c>true</c> (default), records exceptions on AspNetCore, HttpClient, and SqlClient spans.
    /// Applied before the matching <c>Configure*</c> callbacks.
    /// </summary>
    public bool RecordException { get; set; } = true;

    /// <summary>
    /// Advanced ASP.NET Core tracing options (Enrich, Filter, etc.).
    /// Invoked after Filter / <see cref="RecordException"/> defaults and before
    /// <see cref="TelemetryComponentTags.TagName"/> enrichment.
    /// </summary>
    public Action<AspNetCoreTraceInstrumentationOptions>? ConfigureAspNetCore { get; set; }

    /// <summary>
    /// HttpClient instrumentation. Default: <c>true</c>.
    /// </summary>
    public bool HttpClient { get; set; } = true;

    /// <summary>
    /// Advanced HttpClient tracing options.
    /// Invoked after <see cref="RecordException"/> defaults and before
    /// <see cref="TelemetryComponentTags.TagName"/> enrichment.
    /// </summary>
    public Action<HttpClientTraceInstrumentationOptions>? ConfigureHttpClient { get; set; }

    /// <summary>
    /// .NET runtime metrics. Default: <c>true</c>.
    /// </summary>
    public bool Runtime { get; set; } = true;

    /// <summary>
    /// Npgsql (PostgreSQL) tracing via <c>Npgsql.OpenTelemetry</c>. Default: <c>true</c>.
    /// </summary>
    public bool Npgsql { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), registers built-in ASP.NET / BCL meters
    /// (Hosting, Kestrel, Routing, Diagnostics, Authentication, Authorization, MemoryPool,
    /// <c>System.Net.Http</c>, DNS) as in Aspire ServiceDefaults / Microsoft samples.
    /// </summary>
    public bool IncludeFrameworkMeters { get; set; } = true;

    /// <summary>
    /// SqlClient tracing and <c>db.client.operation.duration</c> metrics
    /// (<c>OpenTelemetry.Instrumentation.SqlClient</c>, stable). Default: <c>false</c> — opt-in.
    /// </summary>
    public bool SqlClient { get; set; }

    /// <summary>
    /// Advanced SqlClient tracing options.
    /// Invoked after <see cref="RecordException"/> defaults and before
    /// <see cref="TelemetryComponentTags.TagName"/> enrichment.
    /// </summary>
    public Action<SqlClientTraceInstrumentationOptions>? ConfigureSqlClient { get; set; }

    /// <summary>
    /// Registers ActivitySource <see cref="TelemetryDefaults.MassTransitActivitySource"/> for MassTransit 8+.
    /// Default: <c>false</c> — opt-in. No contrib package; source-only.
    /// </summary>
    public bool MassTransit { get; set; }

    /// <summary>
    /// Registers ActivitySource <see cref="TelemetryDefaults.EventBusActivitySource"/>
    /// (e.g. EventBusRabbitMQ <c>ProcessMessage</c>). Default: <c>false</c> — opt-in. Source-only.
    /// </summary>
    public bool EventBus { get; set; }
}
