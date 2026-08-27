# BuildingBlocks.Telemetry — agent notes

Config-driven OpenTelemetry traces, metrics, and logs for .NET 8/9/10. Install: `dotnet add package BuildingBlocks.Telemetry`. Requires `IHostApplicationBuilder` (`Microsoft.AspNetCore.App`).

## When to choose this

One `AddTelemetry` call for ASP.NET Core / HttpClient / Runtime / Npgsql (SqlClient opt-in), plus source-only Mediator / EventBus / MassTransit. Exporters: OTLP (env fast-path), Console, Azure Monitor.

## Register

```csharp
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true; // AddSource + AddMeter for BuildingBlocks.Mediator
    o.Instrumentation.Npgsql = true;
});
```

OTLP turns on when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Prefer env over hard-coded endpoints.

## 1.0.1

`IntegrateMediator` also `AddMeter("BuildingBlocks.Mediator")` so mediator Send metrics export with traces. Constant: `TelemetryDefaults.MediatorMeter`.
