using BuildingBlocks.Telemetry;
using BuildingBlocks.Telemetry.Internal.Exporters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests.Options;

public sealed class TelemetryEnterpriseOptionsTests
{
    [Fact]
    public void CanUseOtlpExporterFastPath_when_env_only()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            })
            .Build();

        var options = new TelemetryOptions();
        Assert.True(OtlpExporterRegistration.CanUseOtlpExporterFastPath(options, config));
    }

    [Fact]
    public void CanUseOtlpExporterFastPath_false_when_console_enabled()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            })
            .Build();

        var options = new TelemetryOptions();
        options.Exporters.Console.Enabled = true;
        Assert.False(OtlpExporterRegistration.CanUseOtlpExporterFastPath(options, config));
    }

    [Fact]
    public void CanUseOtlpExporterFastPath_false_when_explicit_endpoint()
    {
        var config = new ConfigurationBuilder().Build();
        var options = new TelemetryOptions();
        options.Exporters.Otlp.Enabled = true;
        options.Exporters.Otlp.Endpoint = "http://collector:4317";
        Assert.False(OtlpExporterRegistration.CanUseOtlpExporterFastPath(options, config));
    }

    [Fact]
    public void Development_defaults_enable_always_on_and_enterprise_flags()
    {
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry();

        using var app = host.Build();
        var options = app.Services.GetRequiredService<TelemetryOptions>();

        Assert.True(options.AlwaysOnSamplerInDevelopment);
        Assert.True(options.SetErrorStatusOnException);
        Assert.True(options.EnableTraceBasedExemplars);
        Assert.True(options.Instrumentation.RecordException);
        Assert.Null(options.TracesSamplerRatio);
        Assert.False(options.Exporters.AzureMonitor.Enabled);
        Assert.True(options.Instrumentation.IncludeFrameworkMeters);
    }

    [Fact]
    public void ShouldUseAzureMonitor_when_connection_string_env_set()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            })
            .Build();

        var options = new TelemetryOptions();
        Assert.True(AzureMonitorExporterRegistration.ShouldUseAzureMonitor(options, config));
        Assert.Equal(
            "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            AzureMonitorExporterRegistration.ResolveConnectionString(options, config));
    }

    [Fact]
    public void Azure_does_not_disable_otlp_fast_path()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
            })
            .Build();

        var options = new TelemetryOptions();
        options.Exporters.AzureMonitor.Enabled = true;
        options.Exporters.AzureMonitor.ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
        Assert.True(OtlpExporterRegistration.CanUseOtlpExporterFastPath(options, config));
    }

    [Fact]
    public void ConfigureResource_hook_is_registered()
    {
        var invoked = false;
        var host = new HostApplicationBuilder();
        host.AddTelemetry(o =>
            {
                o.EnableMetrics = false;
                o.EnableTracing = false;
                o.EnableLogging = false;
            })
            .ConfigureResource(_ => invoked = true);

        using var app = host.Build();
        // Resource callbacks run when providers configure; with pillars off, force via TelemetryBuilder.
        var tb = app.Services.GetRequiredService<TelemetryBuilder>();
        var rb = OpenTelemetry.Resources.ResourceBuilder.CreateDefault();
        tb.ApplyResource(rb);
        Assert.True(invoked);
    }
}
