using System.Diagnostics;
using BuildingBlocks.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests.AddTelemetry;

public sealed class TelemetryBuilderHookTests
{
    [Fact]
    public void ConfigureTracing_hook_is_invoked_when_provider_builds()
    {
        var hookInvoked = false;
        var exported = new List<Activity>();

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
            })
            .AddSource("BuildingBlocks.Telemetry.Tests")
            .ConfigureTracing(tracing =>
            {
                hookInvoked = true;
                tracing.AddInMemoryExporter(exported);
            });

        using var app = host.Build();
        _ = app.Services.GetService<TracerProvider>();

        Assert.True(hookInvoked);

        using var source = new ActivitySource("BuildingBlocks.Telemetry.Tests");
        using (var activity = source.StartActivity("test-span"))
        {
            Assert.NotNull(activity);
            activity!.SetTag("test", true);
        }

        app.Services.GetService<TracerProvider>()?.ForceFlush();

        Assert.Contains(exported, a => a.OperationName == "test-span");
    }

    [Fact]
    public void ConfigureTracing_can_register_services_when_the_host_starts()
    {
        var servicesConfigured = false;
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
            })
            .ConfigureTracing(tracing => tracing.ConfigureServices(_ => servicesConfigured = true));

        using var app = host.Build();
        _ = app.Services.GetService<TracerProvider>();

        Assert.True(servicesConfigured);
    }
}
