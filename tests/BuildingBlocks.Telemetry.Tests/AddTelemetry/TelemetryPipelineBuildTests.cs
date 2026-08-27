using BuildingBlocks.Telemetry;
using BuildingBlocks.Telemetry.Internal.Exporters;
using BuildingBlocks.Telemetry.Internal.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests.AddTelemetry;

public sealed class TelemetryPipelineBuildTests
{
    [Fact]
    public void Host_Build_succeeds_with_env_otlp_fast_path()
    {
        var host = new HostApplicationBuilder();
        host.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
        });
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o =>
        {
            o.Exporters.Console.Enabled = false;
            o.Instrumentation.AspNetCore = false;
            o.Instrumentation.HttpClient = false;
            o.Instrumentation.Runtime = false;
            o.Instrumentation.Npgsql = false;
        });

        using var app = host.Build();
        Assert.NotNull(app.Services.GetService<TracerProvider>());
        Assert.NotNull(app.Services.GetService<MeterProvider>());
        Assert.True(
            OtlpExporterRegistration.CanUseOtlpExporterFastPath(
                app.Services.GetRequiredService<TelemetryOptions>(),
                host.Configuration));
    }

    [Fact]
    public void Host_Build_succeeds_with_console_and_explicit_otlp_per_signal()
    {
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o =>
        {
            o.Exporters.Console.Enabled = true;
            o.Exporters.Otlp.Enabled = true;
            o.Exporters.Otlp.Endpoint = "http://localhost:4318";
            o.Instrumentation.AspNetCore = false;
            o.Instrumentation.HttpClient = false;
            o.Instrumentation.Runtime = false;
            o.Instrumentation.Npgsql = false;
        });

        using var app = host.Build();
        var options = app.Services.GetRequiredService<TelemetryOptions>();
        Assert.False(OtlpExporterRegistration.CanUseOtlpExporterFastPath(options, host.Configuration));
        Assert.NotNull(app.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void ValidateOnStart_fails_on_bad_sampler_ratio()
    {
        var host = new HostApplicationBuilder();
        host.AddTelemetry();
        host.Services.PostConfigure<TelemetryOptions>(o => o.TracesSamplerRatio = 1.5);

        using var app = host.Build();
        Assert.Throws<OptionsValidationException>(() =>
            _ = app.Services.GetRequiredService<IOptions<TelemetryOptions>>().Value);
    }

    [Fact]
    public void ValidateOnStart_fails_on_relative_otlp_uri()
    {
        var host = new HostApplicationBuilder();
        host.AddTelemetry();
        host.Services.PostConfigure<TelemetryOptions>(o =>
            o.Exporters.Otlp.Endpoint = "relative/path");

        using var app = host.Build();
        Assert.Throws<OptionsValidationException>(() =>
            _ = app.Services.GetRequiredService<IOptions<TelemetryOptions>>().Value);
    }

    [Fact]
    public void TracesSamplerRatio_is_preserved_when_set()
    {
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o => o.TracesSamplerRatio = 0.25);

        using var app = host.Build();
        var options = app.Services.GetRequiredService<TelemetryOptions>();
        Assert.Equal(0.25, options.TracesSamplerRatio);
    }

    [Fact]
    public void Pillar_toggles_disable_providers()
    {
        var host = new HostApplicationBuilder();
        host.AddTelemetry(o =>
        {
            o.EnableTracing = false;
            o.EnableMetrics = false;
            o.EnableLogging = false;
        });

        using var app = host.Build();
        Assert.Null(app.Services.GetService<TracerProvider>());
        Assert.Null(app.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void AddMeter_and_AddSource_hooks_apply()
    {
        var sourceSeen = false;
        var meterSeen = false;

        var host = new HostApplicationBuilder();
        host.AddTelemetry(o =>
            {
                o.Exporters.Otlp.Enabled = false;
                o.Exporters.Console.Enabled = false;
                o.Instrumentation.AspNetCore = false;
                o.Instrumentation.HttpClient = false;
                o.Instrumentation.Runtime = false;
                o.Instrumentation.Npgsql = false;
            })
            .AddSource("BuildingBlocks.Telemetry.Tests.ExtraSource")
            .AddMeter("BuildingBlocks.Telemetry.Tests.ExtraMeter")
            .ConfigureTracing(t =>
            {
                sourceSeen = true;
                t.AddSource("BuildingBlocks.Telemetry.Tests.ExtraSource");
            })
            .ConfigureMetrics(m =>
            {
                meterSeen = true;
                m.AddMeter("BuildingBlocks.Telemetry.Tests.ExtraMeter");
            });

        using var app = host.Build();
        _ = app.Services.GetService<TracerProvider>();
        _ = app.Services.GetService<MeterProvider>();

        Assert.True(sourceSeen);
        Assert.True(meterSeen);
    }

    [Fact]
    public void Resource_includes_deployment_environment()
    {
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Staging;
        host.AddTelemetry(o =>
        {
            o.EnableMetrics = false;
            o.EnableLogging = false;
            o.Exporters.Otlp.Enabled = false;
            o.Exporters.Console.Enabled = false;
            o.Instrumentation.AspNetCore = false;
            o.Instrumentation.HttpClient = false;
            o.Instrumentation.Runtime = false;
            o.Instrumentation.Npgsql = false;
        });

        using var app = host.Build();
        var options = app.Services.GetRequiredService<TelemetryOptions>();
        var resource = TelemetryResourceConfigurator
            .CreateResourceBuilder("test-svc", options, host.Environment)
            .Build();

        Assert.Contains(
            resource.Attributes,
            a => a.Key == "deployment.environment" && Equals(a.Value, Environments.Staging));
    }

    [Fact]
    public void Default_ExcludedPathPrefixes_include_health_alive_ready_metrics()
    {
        var options = new TelemetryInstrumentationOptions();
        Assert.Contains("/health", options.ExcludedPathPrefixes);
        Assert.Contains("/alive", options.ExcludedPathPrefixes);
        Assert.Contains("/ready", options.ExcludedPathPrefixes);
        Assert.Contains("/metrics", options.ExcludedPathPrefixes);
    }

    [Fact]
    public void Host_Build_succeeds_with_opt_in_extras()
    {
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o =>
        {
            o.Exporters.Otlp.Enabled = false;
            o.Exporters.Console.Enabled = false;
            o.Instrumentation.AspNetCore = false;
            o.Instrumentation.HttpClient = false;
            o.Instrumentation.Runtime = false;
            o.Instrumentation.Npgsql = false;
            o.Instrumentation.SqlClient = true;
            o.Instrumentation.MassTransit = true;
            o.Instrumentation.EventBus = true;
        });

        using var app = host.Build();
        Assert.NotNull(app.Services.GetService<TracerProvider>());
        Assert.NotNull(app.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void ConfigureAspNetCore_is_invoked_when_enabled()
    {
        var configureInvoked = false;
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o =>
        {
            o.Exporters.Otlp.Enabled = false;
            o.Exporters.Console.Enabled = false;
            o.Instrumentation.HttpClient = false;
            o.Instrumentation.Runtime = false;
            o.Instrumentation.Npgsql = false;
            o.Instrumentation.ConfigureAspNetCore = _ => configureInvoked = true;
        });

        using var app = host.Build();
        Assert.NotNull(app.Services.GetService<TracerProvider>());
        Assert.True(configureInvoked);
    }

    [Fact]
    public void ConfigureHttpClient_is_invoked_when_enabled()
    {
        var configureInvoked = false;
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o =>
        {
            o.Exporters.Otlp.Enabled = false;
            o.Exporters.Console.Enabled = false;
            o.Instrumentation.AspNetCore = false;
            o.Instrumentation.Runtime = false;
            o.Instrumentation.Npgsql = false;
            o.Instrumentation.ConfigureHttpClient = _ => configureInvoked = true;
        });

        using var app = host.Build();
        Assert.NotNull(app.Services.GetService<TracerProvider>());
        Assert.True(configureInvoked);
    }

    [Fact]
    public void ConfigureSqlClient_is_invoked_when_enabled()
    {
        var configureInvoked = false;
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o =>
        {
            o.Exporters.Otlp.Enabled = false;
            o.Exporters.Console.Enabled = false;
            o.Instrumentation.AspNetCore = false;
            o.Instrumentation.HttpClient = false;
            o.Instrumentation.Runtime = false;
            o.Instrumentation.Npgsql = false;
            o.Instrumentation.SqlClient = true;
            o.Instrumentation.ConfigureSqlClient = _ => configureInvoked = true;
        });

        using var app = host.Build();
        Assert.NotNull(app.Services.GetService<TracerProvider>());
        Assert.True(configureInvoked);
    }
}
