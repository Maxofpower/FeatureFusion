using BuildingBlocks.Telemetry;
using BuildingBlocks.Telemetry.Internal.Exporters;
using BuildingBlocks.Telemetry.Internal.Options;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests.Options;

public sealed class TelemetryOptionsFactoryTests
{
    [Fact]
    public void Rejects_invalid_sampler_ratio()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TelemetryOptionsFactory.Create(config, o => o.TracesSamplerRatio = 1.5));
    }

    [Fact]
    public void Rejects_non_absolute_otlp_endpoint()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentException>(() =>
            TelemetryOptionsFactory.Create(config, o => o.Exporters.Otlp.Endpoint = "not-a-uri"));
    }

    [Fact]
    public void ShouldUseOtlp_when_env_endpoint_present()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            })
            .Build();

        var options = new TelemetryOptions();
        Assert.True(OtlpExporterRegistration.ShouldUseOtlp(options, config));
    }

    [Fact]
    public void Rejects_azure_monitor_enabled_without_connection_string()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentException>(() =>
            TelemetryOptionsFactory.Create(config, o => o.Exporters.AzureMonitor.Enabled = true));
    }

    [Fact]
    public void Binds_application_insights_connection_string_from_env()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            })
            .Build();

        var options = TelemetryOptionsFactory.Create(config, configure: null);
        Assert.Equal(
            "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            options.Exporters.AzureMonitor.ConnectionString);
    }

    [Fact]
    public void ExcludedPathPrefixes_default_includes_metrics()
    {
        var config = new ConfigurationBuilder().Build();
        var options = TelemetryOptionsFactory.Create(config, configure: null);
        Assert.Contains("/metrics", options.Instrumentation.ExcludedPathPrefixes);
    }
}
