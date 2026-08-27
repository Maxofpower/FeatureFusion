using BuildingBlocks.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.ServiceDiscovery;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    /// <summary>
    /// Adds Aspire service defaults. OpenTelemetry is configured via <see cref="TelemetryHostBuilderExtensions.AddTelemetry"/>.
    /// </summary>
    /// <param name="builder">Host builder.</param>
    /// <param name="configureTelemetry">
    /// Optional Fluent hooks (sources, meters, consumer-added contrib instrumentations).
    /// </param>
    public static TBuilder AddServiceDefaults<TBuilder>(
        this TBuilder builder,
        Action<TelemetryBuilder>? configureTelemetry = null)
        where TBuilder : IHostApplicationBuilder
        => builder.AddServiceDefaults(configureOptions: null, configureTelemetry);

    /// <summary>
    /// Adds Aspire service defaults with dynamic Telemetry options (food-delivery style) and optional builder hooks.
    /// </summary>
    /// <param name="builder">Host builder.</param>
    /// <param name="configureOptions">
    /// Options overrides: pillars, instrumentations, OTLP endpoint/protocol/headers, sampling.
    /// Same idea as food-delivery <c>AddCustomOpenTelemetry(Action&lt;OpenTelemetryOptions&gt;)</c>.
    /// Also bindable from <c>Telemetry</c> config / <c>OTEL_*</c> env without this callback.
    /// </param>
    /// <param name="configureTelemetry">
    /// Optional Fluent hooks for app-owned sources and extra instrumentations.
    /// </param>
    public static TBuilder AddServiceDefaults<TBuilder>(
        this TBuilder builder,
        Action<TelemetryOptions>? configureOptions,
        Action<TelemetryBuilder>? configureTelemetry = null)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddTelemetry(configureOptions ?? (_ => { }), configureTelemetry);

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry via <see cref="TelemetryHostBuilderExtensions.AddTelemetry"/>.
    /// Prefer <see cref="AddServiceDefaults{TBuilder}(TBuilder, Action{TelemetryBuilder}?)"/> which already calls this.
    /// </summary>
    public static TelemetryBuilder ConfigureOpenTelemetry<TBuilder>(
        this TBuilder builder,
        Action<TelemetryOptions>? configureOptions = null)
        where TBuilder : IHostApplicationBuilder
        => builder.AddTelemetry(configureOptions);

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");

            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
