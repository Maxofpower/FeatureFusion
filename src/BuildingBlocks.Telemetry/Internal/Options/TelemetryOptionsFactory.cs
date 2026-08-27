using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Telemetry.Internal.Options;

/// <summary>
/// Creates a validated snapshot of <see cref="TelemetryOptions"/> for pipeline registration.
/// </summary>
internal static class TelemetryOptionsFactory
{
    /// <summary>
    /// Binds the <see cref="TelemetryOptions.SectionName"/> section, applies <paramref name="configure"/>,
    /// normalizes null collections, and eagerly validates sampler / OTLP URI / Azure Monitor rules.
    /// </summary>
    public static TelemetryOptions Create(IConfiguration configuration, Action<TelemetryOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new TelemetryOptions();
        configuration.GetSection(TelemetryOptions.SectionName).Bind(options);
        configure?.Invoke(options);

        if (options.Instrumentation is null)
        {
            options.Instrumentation = new TelemetryInstrumentationOptions();
        }

        if (options.Exporters is null)
        {
            options.Exporters = new TelemetryExporterOptions();
        }

        options.Sources ??= [];
        options.Meters ??= [];
        options.ResourceAttributes ??= new Dictionary<string, string>(StringComparer.Ordinal);
        options.Instrumentation.ExcludedPathPrefixes ??=
            TelemetryInstrumentationOptions.CreateDefaultExcludedPathPrefixes();

        if (options.Exporters.AzureMonitor is null)
        {
            options.Exporters.AzureMonitor = new TelemetryAzureMonitorExporterOptions();
        }

        if (string.IsNullOrWhiteSpace(options.Exporters.AzureMonitor.ConnectionString))
        {
            options.Exporters.AzureMonitor.ConnectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        }

        if (options.TracesSamplerRatio is { } ratio && (ratio < 0.0 || ratio > 1.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(TelemetryOptions.TracesSamplerRatio),
                ratio,
                "Must be between 0.0 and 1.0 inclusive.");
        }

        if (!string.IsNullOrWhiteSpace(options.Exporters.Otlp.Endpoint)
            && !Uri.TryCreate(options.Exporters.Otlp.Endpoint, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "Otlp.Endpoint must be an absolute URI when set.",
                nameof(TelemetryOptions.Exporters));
        }

        if (options.Exporters.AzureMonitor.Enabled
            && string.IsNullOrWhiteSpace(options.Exporters.AzureMonitor.ConnectionString)
            && string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            throw new ArgumentException(
                "Azure Monitor is enabled but no connection string was provided. Set Exporters.AzureMonitor.ConnectionString or APPLICATIONINSIGHTS_CONNECTION_STRING.",
                nameof(TelemetryOptions.Exporters));
        }

        return options;
    }
}
