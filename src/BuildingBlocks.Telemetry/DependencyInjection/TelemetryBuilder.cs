using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Telemetry;

/// <summary>
/// Fluent hooks returned by <c>AddTelemetry</c> for custom sources, meters, and escape-hatch configuration.
/// </summary>
/// <remarks>
/// <para>
/// Registration methods are thread-safe. Apply methods take a snapshot under the same lock so
/// concurrent registration during host build cannot tear the callback lists.
/// </para>
/// <para>
/// Prefer <see cref="TelemetryInstrumentationOptions"/> flags and <c>Configure*</c> callbacks for
/// AspNetCore, HttpClient, and SqlClient. Use <see cref="ConfigureTracing"/> /
/// <see cref="ConfigureMetrics"/> for instrumentations or processors not covered by those options
/// (for example EF Core, Redis, or gRPC contrib packages added by the consumer).
/// </para>
/// </remarks>
public sealed class TelemetryBuilder
{
    private readonly IServiceCollection _services;
    private readonly object _gate = new();
    private readonly List<Action<TracerProviderBuilder>> _tracingCallbacks = [];
    private readonly List<Action<MeterProviderBuilder>> _metricsCallbacks = [];
    private readonly List<Action<OpenTelemetryLoggerOptions>> _loggingCallbacks = [];
    private readonly List<Action<ResourceBuilder>> _resourceCallbacks = [];
    private readonly List<string> _extraSources = [];
    private readonly List<string> _extraMeters = [];

    internal TelemetryBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// The service collection being configured.
    /// </summary>
    public IServiceCollection Services => _services;

    /// <summary>
    /// Registers additional OpenTelemetry resource configuration (detectors, attributes).
    /// </summary>
    /// <param name="configure">Callback invoked when the resource builder is applied.</param>
    /// <returns>The same <see cref="TelemetryBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public TelemetryBuilder ConfigureResource(Action<ResourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        lock (_gate)
        {
            _resourceCallbacks.Add(configure);
        }

        // Register now: AddOpenTelemetry().ConfigureResource runs during IServiceCollection
        // setup. A snapshot at AddTelemetry() would miss fluent callbacks chained afterwards.
        _services.AddOpenTelemetry().ConfigureResource(configure);
        return this;
    }

    /// <summary>
    /// Registers additional tracing configuration not covered by <see cref="TelemetryInstrumentationOptions"/>.
    /// </summary>
    /// <param name="configure">Callback invoked when the tracer provider is configured.</param>
    /// <returns>The same <see cref="TelemetryBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public TelemetryBuilder ConfigureTracing(Action<TracerProviderBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        lock (_gate)
        {
            _tracingCallbacks.Add(configure);
        }

        // Must run while IServiceCollection is still open. Contrib instrumentations
        // (EF Core, Redis) call TracerProviderBuilder.ConfigureServices, which throws
        // after the host ServiceProvider exists. AddOpenTelemetry().WithTracing here
        // is that early window; ConfigureOpenTelemetryTracerProvider((sp, b) => ...) is not.
        _services.AddOpenTelemetry().WithTracing(configure);
        return this;
    }

    /// <summary>
    /// Registers additional metrics configuration not covered by <see cref="TelemetryInstrumentationOptions"/>.
    /// </summary>
    /// <param name="configure">Callback invoked when the meter provider is configured.</param>
    /// <returns>The same <see cref="TelemetryBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public TelemetryBuilder ConfigureMetrics(Action<MeterProviderBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        lock (_gate)
        {
            _metricsCallbacks.Add(configure);
        }

        _services.AddOpenTelemetry().WithMetrics(configure);
        return this;
    }

    /// <summary>
    /// Registers additional OpenTelemetry logging configuration.
    /// </summary>
    /// <param name="configure">Callback invoked when OpenTelemetry logging options are configured.</param>
    /// <returns>The same <see cref="TelemetryBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public TelemetryBuilder ConfigureLogging(Action<OpenTelemetryLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        lock (_gate)
        {
            _loggingCallbacks.Add(configure);
        }

        return this;
    }

    /// <summary>
    /// Adds an ActivitySource name to the tracer provider.
    /// </summary>
    /// <param name="name">ActivitySource name (must not be null or whitespace).</param>
    /// <returns>The same <see cref="TelemetryBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public TelemetryBuilder AddSource(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            _extraSources.Add(name);
        }

        _services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource(name));
        return this;
    }

    /// <summary>
    /// Adds a Meter name to the meter provider.
    /// </summary>
    /// <param name="name">Meter name (must not be null or whitespace).</param>
    /// <returns>The same <see cref="TelemetryBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public TelemetryBuilder AddMeter(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            _extraMeters.Add(name);
        }

        _services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(name));
        return this;
    }

    /// <summary>
    /// Applies registered resource callbacks (used by the pipeline and tests).
    /// </summary>
    internal void ApplyResource(ResourceBuilder resourceBuilder)
    {
        ArgumentNullException.ThrowIfNull(resourceBuilder);

        Action<ResourceBuilder>[] callbacks;
        lock (_gate)
        {
            callbacks = [.. _resourceCallbacks];
        }

        foreach (var callback in callbacks)
        {
            callback(resourceBuilder);
        }
    }

    /// <summary>
    /// Applies extra sources and tracing callbacks.
    /// </summary>
    internal void ApplyTracing(TracerProviderBuilder tracing)
    {
        ArgumentNullException.ThrowIfNull(tracing);

        string[] sources;
        Action<TracerProviderBuilder>[] callbacks;
        lock (_gate)
        {
            sources = [.. _extraSources];
            callbacks = [.. _tracingCallbacks];
        }

        foreach (var source in sources)
        {
            tracing.AddSource(source);
        }

        foreach (var callback in callbacks)
        {
            callback(tracing);
        }
    }

    /// <summary>
    /// Applies extra meters and metrics callbacks.
    /// </summary>
    internal void ApplyMetrics(MeterProviderBuilder metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        string[] meters;
        Action<MeterProviderBuilder>[] callbacks;
        lock (_gate)
        {
            meters = [.. _extraMeters];
            callbacks = [.. _metricsCallbacks];
        }

        foreach (var meter in meters)
        {
            metrics.AddMeter(meter);
        }

        foreach (var callback in callbacks)
        {
            callback(metrics);
        }
    }

    /// <summary>
    /// Applies logging callbacks.
    /// </summary>
    internal void ApplyLogging(OpenTelemetryLoggerOptions logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        Action<OpenTelemetryLoggerOptions>[] callbacks;
        lock (_gate)
        {
            callbacks = [.. _loggingCallbacks];
        }

        foreach (var callback in callbacks)
        {
            callback(logging);
        }
    }
}
