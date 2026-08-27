using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry.Internal.Exporters;

/// <summary>
/// Azure Monitor / Application Insights enablement and per-signal exporter registration.
/// </summary>
internal static class AzureMonitorExporterRegistration
{
    /// <summary>
    /// Azure Monitor is on when explicitly enabled or a connection string is present
    /// (Aspire ServiceDefaults pattern: <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c>).
    /// </summary>
    public static bool ShouldUseAzureMonitor(TelemetryOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        return options.Exporters.AzureMonitor.Enabled
            || !string.IsNullOrWhiteSpace(options.Exporters.AzureMonitor.ConnectionString)
            || !string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]);
    }

    /// <summary>
    /// Resolves the Application Insights connection string from options or environment.
    /// </summary>
    public static string? ResolveConnectionString(TelemetryOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(options.Exporters.AzureMonitor.ConnectionString))
        {
            return options.Exporters.AzureMonitor.ConnectionString;
        }

        return configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
    }

    /// <summary>
    /// Registers Azure Monitor exporters for enabled signals when a connection string is available.
    /// </summary>
    public static void Register(IHostApplicationBuilder builder, TelemetryOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!ShouldUseAzureMonitor(options, configuration))
        {
            return;
        }

        var connectionString = ResolveConnectionString(options, configuration);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (options.EnableTracing)
        {
            builder.Services.ConfigureOpenTelemetryTracerProvider((_, tracing) =>
                tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = connectionString));
        }

        if (options.EnableMetrics)
        {
            builder.Services.ConfigureOpenTelemetryMeterProvider((_, metrics) =>
                metrics.AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString));
        }

        if (options.EnableLogging)
        {
            builder.Services.Configure<OpenTelemetryLoggerOptions>(logging =>
                logging.AddAzureMonitorLogExporter(o => o.ConnectionString = connectionString));
        }
    }
}
