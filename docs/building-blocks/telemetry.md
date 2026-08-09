# Telemetry

Optional Send enrichment — **not** a pipeline behavior.

```csharp
services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
    // Omit UseTelemetry entirely for zero ActivitySource overhead.
    cfg.UseTelemetry(o =>
    {
        o.ActivitySourceName = "BuildingBlocks.Mediator"; // optional override
        o.RecordException = true;
        o.EnableLogging = true;
    });
});
```

## What it does

- Starts an `Activity` per `Send` that **wraps the full pipeline + handler** (`Activity.Current` is set inside behaviors and handlers).
- Tags: `mediator.request_name`, `mediator.request_type`, `mediator.message_kind`, `mediator.success`, `mediator.duration_ms`.
- Activity name: `{RequestTypeName} Handling`.
- Optional `ILogger` start/end/error.
- On fault: records exception on the activity (if enabled) and **rethrows**.

## What it does not

- Register an `IPipelineBehavior` (no telemetry slot in the behavior chain).
- Metrics counters/histograms — use a host behavior.
- Exception *handlers* that replace the result — not supported.

## OpenTelemetry / Aspire

Subscribe to the configured `ActivitySource` name:

```csharp
tracing.AddSource("BuildingBlocks.Mediator");
```
