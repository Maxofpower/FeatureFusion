using System.Collections.Concurrent;
using System.Diagnostics;

namespace BuildingBlocks.Telemetry;

/// <summary>
/// Helpers for manual spans and events. Register the source with <see cref="TelemetryBuilder.AddSource"/>
/// (or <see cref="TelemetryOptions.Sources"/>) so they are exported.
/// </summary>
/// <remarks>
/// <see cref="Start"/> reuses a cached <see cref="ActivitySource"/> per name (do not construct
/// <see cref="ActivitySource"/> per call in application code).
/// </remarks>
/// <example>
/// <code>
/// telemetry.AddSource("MyApp");
/// using var activity = TelemetryActivity.Start("MyApp", "Checkout");
/// activity?.SetTag("order.id", orderId);
/// activity?.AddEvent("payment.started");
/// </code>
/// </example>
public static class TelemetryActivity
{
    private static readonly ConcurrentDictionary<string, ActivitySource> Sources = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts a span on <paramref name="sourceName"/>. Returns <see langword="null"/> when no listener is registered.
    /// </summary>
    /// <param name="sourceName">ActivitySource name (must match <c>AddSource</c>).</param>
    /// <param name="operationName">Span name.</param>
    /// <param name="kind">Activity kind. Default: <see cref="ActivityKind.Internal"/>.</param>
    /// <returns>The started activity, or <see langword="null"/> if unused.</returns>
    public static Activity? Start(string sourceName, string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        var source = Sources.GetOrAdd(sourceName, static name => new ActivitySource(name));
        return source.StartActivity(operationName, kind);
    }

    /// <summary>
    /// Adds a named event on the current span (OpenTelemetry <c>AddEvent</c>).
    /// </summary>
    /// <param name="activity">The activity. No-op when <see langword="null"/>.</param>
    /// <param name="name">Event name.</param>
    /// <param name="tags">Optional event attributes.</param>
    /// <returns>The same <paramref name="activity"/> for chaining.</returns>
    public static Activity? AddEvent(this Activity? activity, string name, params KeyValuePair<string, object?>[] tags)
    {
        if (activity is null || string.IsNullOrWhiteSpace(name))
        {
            return activity;
        }

        if (tags is { Length: > 0 })
        {
            activity.AddEvent(new ActivityEvent(name, tags: new ActivityTagsCollection(tags)));
        }
        else
        {
            activity.AddEvent(new ActivityEvent(name));
        }

        return activity;
    }

    /// <summary>
    /// Records an exception on the span (status Error + exception event).
    /// </summary>
    /// <param name="activity">The activity. No-op when <see langword="null"/>.</param>
    /// <param name="exception">The exception to record.</param>
    /// <returns>The same <paramref name="activity"/> for chaining.</returns>
    public static Activity? RecordException(this Activity? activity, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (activity is null)
        {
            return null;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
#if NET9_0_OR_GREATER
        activity.AddException(exception);
#else
#pragma warning disable CS0618
        OpenTelemetry.Trace.ActivityExtensions.RecordException(activity, exception);
#pragma warning restore CS0618
#endif
        return activity;
    }
}
