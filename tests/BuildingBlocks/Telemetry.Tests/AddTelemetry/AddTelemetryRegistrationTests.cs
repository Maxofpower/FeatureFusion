using BuildingBlocks.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests.AddTelemetry;

public sealed class AddTelemetryRegistrationTests
{
    [Fact]
    public void Registers_options_and_defaults()
    {
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry();

        using var app = host.Build();
        var options = app.Services.GetRequiredService<TelemetryOptions>();

        Assert.True(options.IntegrateMediator);
        Assert.True(options.EnableTracing);
        Assert.True(options.EnableMetrics);
        Assert.True(options.EnableLogging);
        Assert.True(options.Instrumentation.FilterHealthCheckRequests);
        Assert.True(options.AlwaysOnSamplerInDevelopment);
        Assert.True(options.SetErrorStatusOnException);
        Assert.True(options.EnableTraceBasedExemplars);
        Assert.Contains("/health", options.Instrumentation.ExcludedPathPrefixes);
        Assert.Contains("/metrics", options.Instrumentation.ExcludedPathPrefixes);
        Assert.False(options.Instrumentation.SqlClient);
        Assert.False(options.Instrumentation.MassTransit);
        Assert.False(options.Instrumentation.EventBus);
        Assert.NotNull(app.Services.GetService<TelemetryBuilder>());
    }

    [Fact]
    public void Respects_configure_callback()
    {
        var host = new HostApplicationBuilder();
        host.AddTelemetry(o =>
        {
            o.IntegrateMediator = false;
            o.EnableMetrics = false;
            o.Instrumentation.AspNetCore = false;
            o.AlwaysOnSamplerInDevelopment = false;
            o.Sources.Add("MyApp.Custom");
        });

        using var app = host.Build();
        var options = app.Services.GetRequiredService<TelemetryOptions>();

        Assert.False(options.IntegrateMediator);
        Assert.False(options.EnableMetrics);
        Assert.False(options.Instrumentation.AspNetCore);
        Assert.False(options.AlwaysOnSamplerInDevelopment);
        Assert.Contains("MyApp.Custom", options.Sources);
    }

    [Fact]
    public void Mediator_activity_source_name_matches_defaults()
    {
        Assert.Equal("BuildingBlocks.Mediator", TelemetryDefaults.MediatorActivitySource);
        Assert.Equal("MassTransit", TelemetryDefaults.MassTransitActivitySource);
        Assert.Equal("EventBus", TelemetryDefaults.EventBusActivitySource);
    }

    [Fact]
    public void Opt_in_extras_can_be_enabled()
    {
        var host = new HostApplicationBuilder();
        host.Environment.EnvironmentName = Environments.Development;
        host.AddTelemetry(o =>
        {
            o.Instrumentation.EventBus = true;
            o.Instrumentation.SqlClient = true;
            o.Instrumentation.MassTransit = true;
        });

        using var app = host.Build();
        var options = app.Services.GetRequiredService<TelemetryOptions>();

        Assert.True(options.Instrumentation.EventBus);
        Assert.True(options.Instrumentation.SqlClient);
        Assert.True(options.Instrumentation.MassTransit);
    }

    [Fact]
    public void Two_arg_AddTelemetry_applies_options_and_builder()
    {
        var builderInvoked = false;
        var host = new HostApplicationBuilder();
        host.AddTelemetry(
            configureOptions: o => o.Instrumentation.EventBus = true,
            configureBuilder: t =>
            {
                builderInvoked = true;
                t.AddSource("DbMigrations");
            });

        using var app = host.Build();
        var options = app.Services.GetRequiredService<TelemetryOptions>();
        Assert.True(options.Instrumentation.EventBus);
        Assert.True(builderInvoked);
        Assert.NotNull(app.Services.GetService<TelemetryBuilder>());
    }
}
