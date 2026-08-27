using BuildingBlocks.Telemetry.Internal.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests.Startup;

public sealed class TelemetryStartupSummaryTests
{
    private const string AzureConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://contoso.example/";

    [Fact]
    public void Summary_reports_service_signals_exporters_and_instrumentation()
    {
        var options = new TelemetryOptions
        {
            Instrumentation = { SqlClient = true, EventBus = true },
            Exporters = { Console = { Enabled = true } },
        };
        options.Sources.Add("MyApp.Custom");

        var summary = TelemetryStartupSummary.Create("catalog-api", "Development", options, EmptyConfiguration());

        Assert.Equal("catalog-api", summary.ServiceName);
        Assert.Equal("Development", summary.Environment);
        Assert.Equal(new[] { "traces", "metrics", "logs" }, summary.Signals);
        Assert.Equal(new[] { TelemetryStartupSummary.ConsoleExporter }, summary.Exporters);
        Assert.Contains(TelemetryComponentTags.AspNetCore, summary.Instrumentation);
        Assert.Contains(TelemetryComponentTags.SqlClient, summary.Instrumentation);
        Assert.Contains(TelemetryComponentTags.EventBus, summary.Instrumentation);
        Assert.Contains(TelemetryComponentTags.Mediator, summary.Instrumentation);
        Assert.Contains("1 custom source(s)", summary.Instrumentation);
        Assert.DoesNotContain(TelemetryComponentTags.MassTransit, summary.Instrumentation);
        Assert.True(summary.HasExporter);
    }

    [Fact]
    public void Summary_reports_environment_driven_otlp()
    {
        var configuration = Configuration(("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317"));

        var summary = TelemetryStartupSummary.Create("api", "Production", new TelemetryOptions(), configuration);

        Assert.Equal(new[] { TelemetryStartupSummary.OtlpEnvironmentExporter }, summary.Exporters);
    }

    [Fact]
    public void Summary_reports_explicit_otlp_and_azure_monitor()
    {
        var options = new TelemetryOptions
        {
            Exporters =
            {
                Otlp = { Endpoint = "http://collector.internal:4317", Headers = "authorization=Bearer secret-token" },
                AzureMonitor = { ConnectionString = AzureConnectionString },
            },
        };

        var summary = TelemetryStartupSummary.Create("api", "Production", options, EmptyConfiguration());

        Assert.Equal(
            new[] { TelemetryStartupSummary.OtlpExplicitExporter, TelemetryStartupSummary.AzureMonitorExporter },
            summary.Exporters);
    }

    [Fact]
    public void Write_warns_when_no_exporter_is_configured()
    {
        var summary = TelemetryStartupSummary.Create(
            "api", "Production", new TelemetryOptions(), EmptyConfiguration());
        var logger = new RecordingLogger();

        summary.Write(logger);

        Assert.False(summary.HasExporter);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("no exporter is configured", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_does_not_warn_when_an_exporter_is_configured()
    {
        var options = new TelemetryOptions { Exporters = { Console = { Enabled = true } } };
        var summary = TelemetryStartupSummary.Create("api", "Development", options, EmptyConfiguration());
        var logger = new RecordingLogger();

        summary.Write(logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("api", entry.Message, StringComparison.Ordinal);
        Assert.Contains(TelemetryStartupSummary.ConsoleExporter, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_never_logs_endpoints_headers_or_connection_strings()
    {
        var options = new TelemetryOptions
        {
            Exporters =
            {
                Otlp = { Endpoint = "http://collector.internal:4317", Headers = "authorization=Bearer secret-token" },
                AzureMonitor = { ConnectionString = AzureConnectionString },
            },
        };
        var summary = TelemetryStartupSummary.Create("api", "Production", options, EmptyConfiguration());
        var logger = new RecordingLogger();

        summary.Write(logger);

        var logged = string.Join('\n', logger.Entries.Select(e => e.Message));
        Assert.DoesNotContain("collector.internal", logged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token", logged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InstrumentationKey", logged, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contoso.example", logged, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summary_reports_no_signals_when_all_are_disabled()
    {
        var options = new TelemetryOptions
        {
            EnableTracing = false,
            EnableMetrics = false,
            EnableLogging = false,
        };
        var summary = TelemetryStartupSummary.Create("api", "Production", options, EmptyConfiguration());
        var logger = new RecordingLogger();

        summary.Write(logger);

        Assert.False(summary.HasSignals);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AddTelemetry_registers_the_startup_reporter_once()
    {
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o => o.Exporters.Console.Enabled = true);
        host.AddTelemetry(o => o.Exporters.Console.Enabled = true);

        using var app = host.Build();

        var reporter = Assert.Single(
            app.Services.GetServices<IHostedService>().OfType<TelemetryStartupSummaryReporter>());
        var summary = app.Services.GetRequiredService<TelemetryStartupSummary>();

        Assert.Equal(Environments.Development, summary.Environment);
        Assert.Contains(TelemetryStartupSummary.ConsoleExporter, summary.Exporters);
        await reporter.StartAsync(CancellationToken.None);
        await reporter.StopAsync(CancellationToken.None);
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private sealed class RecordingLogger : ILogger<TelemetryStartupSummaryReporter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
