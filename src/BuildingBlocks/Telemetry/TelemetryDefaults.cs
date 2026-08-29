namespace BuildingBlocks.Telemetry;

/// <summary>
/// Well-known ActivitySource names used by BuildingBlocks.Telemetry integrations.
/// </summary>
/// <remarks>
/// Libraries emit spans on these sources; this package only registers the names when the
/// matching option is enabled (<see cref="TelemetryOptions.IntegrateMediator"/>,
/// <see cref="TelemetryOptions.IntegrateMcp"/>,
/// <see cref="TelemetryInstrumentationOptions.MassTransit"/>,
/// <see cref="TelemetryInstrumentationOptions.EventBus"/>).
/// </remarks>
public static class TelemetryDefaults
{
    /// <summary>
    /// ActivitySource name used by <c>BuildingBlocks.Mediator</c> when <c>UseTelemetry()</c> is enabled.
    /// Matching this string integrates mediator Send spans without taking a package reference on the mediator.
    /// </summary>
    public const string MediatorActivitySource = "BuildingBlocks.Mediator";

    /// <summary>
    /// Meter name used by <c>BuildingBlocks.Mediator</c> when <c>UseTelemetry()</c> metrics are enabled.
    /// Same value as <see cref="MediatorActivitySource"/> so one <c>IntegrateMediator</c> flag wires traces and metrics.
    /// </summary>
    public const string MediatorMeter = "BuildingBlocks.Mediator";

    /// <summary>
    /// ActivitySource name used by MassTransit 8+ when <see cref="TelemetryInstrumentationOptions.MassTransit"/> is enabled.
    /// </summary>
    public const string MassTransitActivitySource = "MassTransit";

    /// <summary>
    /// ActivitySource name used by EventBusRabbitMQ (<c>ProcessMessage</c>) when
    /// <see cref="TelemetryInstrumentationOptions.EventBus"/> is enabled.
    /// </summary>
    public const string EventBusActivitySource = "EventBus";

    /// <summary>
    /// ActivitySource name used by <c>BuildingBlocks.Mcp</c> when <c>UseTelemetry()</c> is enabled.
    /// </summary>
    public const string McpActivitySource = "BuildingBlocks.Mcp";
}
