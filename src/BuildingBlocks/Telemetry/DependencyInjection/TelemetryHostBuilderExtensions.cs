using BuildingBlocks.Telemetry.Internal.Options;
using BuildingBlocks.Telemetry.Internal.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Telemetry;

/// <summary>
/// Host builder extensions for BuildingBlocks.Telemetry — the production OpenTelemetry entry point.
/// </summary>
/// <remarks>
/// Backend-agnostic: exports via OTLP (<c>OTEL_EXPORTER_OTLP_*</c> or <see cref="TelemetryOptions.Exporters"/>).
/// Local Aspire SigNoz wiring belongs in AppHost (<c>BuildingBlocks.Aspire.Hosting.SigNoz</c>), not here.
/// </remarks>
public static class TelemetryHostBuilderExtensions
{
    /// <summary>
    /// Adds config-driven OpenTelemetry metrics, logging, and tracing.
    /// Reads the <see cref="TelemetryOptions.SectionName"/> section, applies instrumentation
    /// toggles and optional <c>Configure*</c> callbacks, optionally integrates
    /// <see cref="TelemetryDefaults.MediatorActivitySource"/>, and returns a <see cref="TelemetryBuilder"/>
    /// for further customization.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">
    /// Optional callback applied once when the pipeline snapshot is built.
    /// Prefer configuration/<c>OTEL_*</c> for production destinations.
    /// </param>
    /// <returns>
    /// A <see cref="TelemetryBuilder"/> for hooks (<c>ConfigureTracing</c>, <c>AddSource</c>, etc.).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="TelemetryOptions.TracesSamplerRatio"/> is outside 0.0–1.0.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <c>Exporters.Otlp.Endpoint</c> is set but is not an absolute URI,
    /// or Azure Monitor is enabled without a connection string.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Pipeline registration uses a snapshot from configuration + the optional <paramref name="configure"/>
    /// callback at call time. A parallel <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>
    /// registration supports validation-on-start; it does not reconfigure exporters after the host is built.
    /// </para>
    /// <para>
    /// When OTLP is env-driven (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>) and Console / explicit Endpoint/Headers
    /// are off, <c>UseOtlpExporter()</c> registers OTLP for traces, metrics, and logs (Aspire ServiceDefaults path).
    /// Explicit OTLP options or Console force per-signal <c>AddOtlpExporter</c> instead — the two APIs must not mix.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.AddTelemetry();
    ///
    /// builder.AddTelemetry(o => o.TracesSamplerRatio = 0.1)
    ///     .AddSource("MyApp")
    ///     .ConfigureTracing(t => t.AddSource("MyApp.Extra"));
    ///
    /// builder.AddTelemetry(
    ///     configureOptions: o => o.Instrumentation.EventBus = true,
    ///     configureBuilder: t => t.AddSource("DbMigrations"));
    /// </code>
    /// </example>
    public static TelemetryBuilder AddTelemetry(
        this IHostApplicationBuilder builder,
        Action<TelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = TelemetryOptionsFactory.Create(builder.Configuration, configure);

        builder.Services.AddSingleton(options);
        RegisterOptionsValidation(builder, configure);

        var telemetryBuilder = new TelemetryBuilder(builder.Services);
        builder.Services.AddSingleton(telemetryBuilder);

        TelemetryServiceRegistration.Register(builder, options, telemetryBuilder);
        return telemetryBuilder;
    }

    /// <summary>
    /// Adds OpenTelemetry with both options overrides and fluent builder hooks in one call.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configureOptions">
    /// Options overrides (instrumentation flags, exporters, <c>Configure*</c> enrich/filter callbacks).
    /// Also bindable from the <c>Telemetry</c> config section / env (<c>Telemetry__*</c>, <c>OTEL_*</c>).
    /// </param>
    /// <param name="configureBuilder">
    /// Optional escape-hatch hooks for custom sources/meters or instrumentations not covered by options.
    /// Prefer <see cref="TelemetryInstrumentationOptions"/> flags and <c>Configure*</c> when available.
    /// </param>
    /// <returns>The <see cref="TelemetryBuilder"/> after hooks are applied.</returns>
    public static TelemetryBuilder AddTelemetry(
        this IHostApplicationBuilder builder,
        Action<TelemetryOptions> configureOptions,
        Action<TelemetryBuilder>? configureBuilder)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        var telemetryBuilder = builder.AddTelemetry(configureOptions);
        configureBuilder?.Invoke(telemetryBuilder);
        return telemetryBuilder;
    }

    private static void RegisterOptionsValidation(
        IHostApplicationBuilder builder,
        Action<TelemetryOptions>? configure)
    {
        var optionsBuilder = builder.Services
            .AddOptions<TelemetryOptions>()
            .Bind(builder.Configuration.GetSection(TelemetryOptions.SectionName));

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        optionsBuilder
            .Validate(
                static o => o.TracesSamplerRatio is null or (>= 0.0 and <= 1.0),
                $"{nameof(TelemetryOptions.TracesSamplerRatio)} must be between 0.0 and 1.0 inclusive.")
            .Validate(
                static o => o.Instrumentation is not null && o.Exporters is not null,
                "Instrumentation and Exporters must not be null.")
            .Validate(
                static o => string.IsNullOrWhiteSpace(o.Exporters.Otlp.Endpoint)
                    || Uri.TryCreate(o.Exporters.Otlp.Endpoint, UriKind.Absolute, out _),
                $"{nameof(TelemetryOptions.Exporters)}.{nameof(TelemetryExporterOptions.Otlp)}.{nameof(TelemetryOtlpExporterOptions.Endpoint)} must be an absolute URI when set.")
            .Validate(
                static o => o.Instrumentation.ExcludedPathPrefixes is not null,
                $"{nameof(TelemetryInstrumentationOptions.ExcludedPathPrefixes)} must not be null.")
            .Validate(
                static o => !o.Exporters.AzureMonitor.Enabled
                    || !string.IsNullOrWhiteSpace(o.Exporters.AzureMonitor.ConnectionString),
                "Azure Monitor is enabled but ConnectionString is empty. Set Exporters.AzureMonitor.ConnectionString or APPLICATIONINSIGHTS_CONNECTION_STRING.")
            .ValidateOnStart();
    }
}
