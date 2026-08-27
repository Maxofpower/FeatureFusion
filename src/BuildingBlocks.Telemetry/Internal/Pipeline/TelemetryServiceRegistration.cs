using BuildingBlocks.Telemetry.Internal.Exporters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace BuildingBlocks.Telemetry.Internal.Pipeline;

/// <summary>
/// Registers OpenTelemetry logging, metrics, and tracing on an <see cref="IHostApplicationBuilder"/>.
/// </summary>
internal static class TelemetryServiceRegistration
{
    /// <summary>
    /// Wires providers and exporters from a validated options snapshot.
    /// Fluent <see cref="TelemetryBuilder"/> hooks register themselves on <c>AddOpenTelemetry</c>
    /// when called, so contrib <c>ConfigureServices</c> still runs before the host is built.
    /// </summary>
    public static void Register(
        IHostApplicationBuilder builder,
        TelemetryOptions options,
        TelemetryBuilder telemetryBuilder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(telemetryBuilder);

        var serviceName = ResolveServiceName(options, builder.Environment);
        var useOtlp = OtlpExporterRegistration.ShouldUseOtlp(options, builder.Configuration);
        var useOtlpFastPath = OtlpExporterRegistration.CanUseOtlpExporterFastPath(options, builder.Configuration);
        var registerPerSignalOtlp = useOtlp && !useOtlpFastPath;
        var environment = builder.Environment;

        if (options.EnableLogging)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                logging.SetResourceBuilder(
                    TelemetryResourceConfigurator.CreateResourceBuilder(serviceName, options, environment));
                TelemetryPipelineConfigurator.ConfigureLoggingExporters(logging, options, registerPerSignalOtlp);
            });

            builder.Services.Configure<OpenTelemetryLoggerOptions>(telemetryBuilder.ApplyLogging);
        }

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(rb =>
                TelemetryResourceConfigurator.ApplyResource(rb, serviceName, options, environment));

        if (options.EnableMetrics)
        {
            otel.WithMetrics(metrics =>
                TelemetryPipelineConfigurator.ConfigureMetrics(metrics, options, useOtlp, registerPerSignalOtlp));
        }

        if (options.EnableTracing)
        {
            otel.WithTracing(tracing =>
                TelemetryPipelineConfigurator.ConfigureTracing(
                    tracing, serviceName, options, environment, useOtlp, registerPerSignalOtlp));
        }

        if (useOtlpFastPath)
        {
            otel.UseOtlpExporter();
        }

        AzureMonitorExporterRegistration.Register(builder, options, builder.Configuration);
        RegisterStartupSummary(builder, options, serviceName, environment);
    }

    private static void RegisterStartupSummary(
        IHostApplicationBuilder builder,
        TelemetryOptions options,
        string serviceName,
        IHostEnvironment environment)
    {
        builder.Services.AddSingleton(
            TelemetryStartupSummary.Create(
                serviceName,
                environment.EnvironmentName,
                options,
                builder.Configuration));

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, TelemetryStartupSummaryReporter>());
    }

    private static string ResolveServiceName(TelemetryOptions options, IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceName))
        {
            return options.ServiceName;
        }

        return string.IsNullOrWhiteSpace(environment.ApplicationName)
            ? "unknown_service"
            : environment.ApplicationName;
    }
}
