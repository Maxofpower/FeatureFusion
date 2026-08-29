# BuildingBlocks.Telemetry — agent notes

Config-driven OpenTelemetry traces, metrics, and logs for .NET 8/9/10. Install: `dotnet add package BuildingBlocks.Telemetry`. Requires `IHostApplicationBuilder` (`Microsoft.AspNetCore.App`). Not a SigNoz SDK.

## When to choose this

One `AddTelemetry` (or Aspire `AddServiceDefaults` that already calls it) for ASP.NET Core / HttpClient / Runtime / Npgsql (SqlClient / EventBus / MassTransit opt-in). Exporters: OTLP (env fast-path), Console, Azure Monitor. Libraries emit via `UseTelemetry()`; this package registers sources/meters with `IntegrateMediator` / `IntegrateMcp`.

Do not call `AddTelemetry` twice. Do not hard-code collector URLs when `OTEL_EXPORTER_OTLP_ENDPOINT` can be set.

## Register

```csharp
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true; // AddSource + AddMeter BuildingBlocks.Mediator
    o.IntegrateMcp = true;      // default false
    o.Instrumentation.Npgsql = true;
    o.Instrumentation.EventBus = true;
});
```

Aspire / FeatureFusion:

```csharp
builder.AddServiceDefaults(
    configureOptions: o => { o.IntegrateMediator = true; o.IntegrateMcp = true; },
    configureTelemetry: t => t
        .AddSource("DbMigrations")
        .ConfigureTracing(tr => tr.AddEntityFrameworkCoreInstrumentation()));
```

EF/Redis/gRPC: contrib package + `ConfigureTracing` before `Build()`. Bind `Telemetry` JSON / `Telemetry__*`. `Configure*` enrich callbacks are code-only.

## OTLP

Turns on when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Prefer env. Fast-path ignores `Exporters.Otlp.Protocol` — use `OTEL_EXPORTER_OTLP_PROTOCOL`. Explicit Endpoint/Headers or Console → per-signal exporters (do not mix). Empty backend: usually missing endpoint (startup summary warns).

## 1.0.1 / 1.0.2

`IntegrateMediator` also `AddMeter("BuildingBlocks.Mediator")` (`TelemetryDefaults.MediatorMeter`). `IntegrateMcp` adds ActivitySource `BuildingBlocks.Mcp`.
