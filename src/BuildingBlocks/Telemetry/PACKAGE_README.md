# BuildingBlocks.Telemetry

Config-driven OpenTelemetry traces, metrics, and logs for ASP.NET Core from one `AddTelemetry` call. Export **OTLP to any backend**. Requires `IHostApplicationBuilder`.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Telemetry.svg)](https://www.nuget.org/packages/BuildingBlocks.Telemetry)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## What's new in 1.0.2

- `IntegrateMcp` (default **false**) adds ActivitySource `BuildingBlocks.Mcp`
- `TelemetryDefaults.McpActivitySource` / `TelemetryComponentTags.Mcp`

## What's new in 1.0.1

- `IntegrateMediator` also `AddMeter("BuildingBlocks.Mediator")` so Send metrics export with traces
- `TelemetryDefaults.MediatorMeter` (same name as the mediator ActivitySource)

## Requirements

- **.NET 8**, **.NET 9**, or **.NET 10** (`net8.0` / `net9.0` / `net10.0`)
- `IHostApplicationBuilder` (`Microsoft.AspNetCore.App`)

This package is **not** a SigNoz SDK. Local Aspire SigNoz: `BuildingBlocks.Aspire.Hosting.SigNoz`. Libraries **emit** with `UseTelemetry()`; this host **exports** when `IntegrateMediator` / `IntegrateMcp` is on.

## Install

```bash
dotnet add package BuildingBlocks.Telemetry
```

## 1. Quick start — `AddTelemetry`

```csharp
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true;        // default true — source + meter
    o.IntegrateMcp = true;             // default false
    o.Instrumentation.Npgsql = true;
    o.Instrumentation.EventBus = true; // default false
});
```

Options plus fluent hooks:

```csharp
builder.AddTelemetry(
    configureOptions: o => o.EnableMetrics = true,
    configureBuilder: t => t
        .AddSource("DbMigrations")
        .ConfigureTracing(tr => tr.AddEntityFrameworkCoreInstrumentation()));
```

Do not call `AddTelemetry` twice. Aspire hosts that already use FeatureFusion-style `AddServiceDefaults` should pass `configureOptions` / `configureTelemetry` there instead.

## 2. Aspire `AddServiceDefaults`

```csharp
builder.AddServiceDefaults(
    configureOptions: o =>
    {
        o.IntegrateMediator = true;
        o.IntegrateMcp = true;
        o.Instrumentation.Npgsql = true;
        o.Instrumentation.EventBus = true;
    },
    configureTelemetry: telemetry =>
    {
        telemetry.AddSource("DbMigrations");
        telemetry.ConfigureTracing(t => t
            .AddEntityFrameworkCoreInstrumentation()
            .AddRedisInstrumentation());
    });
```

EF Core, Redis, gRPC client, and Prometheus scrape are **not** shipped here. Add the contrib package and `ConfigureTracing` / `ConfigureMetrics` **while `IServiceCollection` is still open** (not after `Build()`).

## 3. Configuration section

The `Telemetry` JSON / `Telemetry__*` env keys bind to `TelemetryOptions`. `ConfigureAspNetCore` / `ConfigureHttpClient` / `ConfigureSqlClient` are **code-only**.

```json
{
  "Telemetry": {
    "EnableTracing": true,
    "EnableMetrics": true,
    "EnableLogging": true,
    "IntegrateMediator": true,
    "IntegrateMcp": false,
    "Instrumentation": {
      "AspNetCore": true,
      "HttpClient": true,
      "Runtime": true,
      "Npgsql": true,
      "SqlClient": false,
      "EventBus": false
    },
    "Exporters": {
      "Otlp": { "Enabled": false },
      "Console": { "Enabled": false }
    }
  }
}
```

## OTLP — prefer env over hard-coded URLs

OTLP turns on when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire `WithSigNozOtlpExporter` sets this on the project). Same binary for local collector, CI, and production.

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
# OTEL_EXPORTER_OTLP_HEADERS=signoz-ingestion-key=...
```

Env-only OTLP uses `UseOtlpExporter()` for traces, metrics, and logs. On that fast-path, `Exporters.Otlp.Protocol` in options is **ignored** — use `OTEL_EXPORTER_OTLP_PROTOCOL`. Setting `Exporters.Otlp.Endpoint` / `Headers` or enabling Console forces per-signal `AddOtlpExporter` (do not mix with the fast-path).

Azure Monitor: `APPLICATIONINSIGHTS_CONNECTION_STRING` or `Exporters.AzureMonitor` (can coexist with OTLP).

## Mediator / MCP

| Library | Emit | Export from this package |
|---------|------|--------------------------|
| `BuildingBlocks.Mediator` | `cfg.UseTelemetry()` | `IntegrateMediator` → `AddSource` + `AddMeter` |
| `BuildingBlocks.Mcp` | MCP builder `UseTelemetry()` | `IntegrateMcp` → `AddSource` |

Without `Integrate*`, spans and meters exist in-process but do not leave the host.

## Enrich / Filter

```csharp
builder.AddTelemetry(o =>
{
    o.Instrumentation.ConfigureAspNetCore = opts =>
    {
        opts.EnrichWithHttpRequest = (activity, request) =>
            activity.SetTag("http.route", request.Path);
    };
    o.Instrumentation.SqlClient = true;
    o.Instrumentation.ConfigureSqlClient = opts =>
    {
        opts.EnrichWithSqlCommand = (activity, command) =>
            activity.SetTag("db.command_type", command.CommandType.ToString());
    };
});
```

Order: Filter / `RecordException` → user callback → `telemetry.component`.

Manual spans (register the source first):

```csharp
telemetry.AddSource("MyApp");
using var activity = TelemetryActivity.Start("MyApp", "Checkout");
activity?.SetTag("order.id", id);
```

## Features

- Tracing, metrics, and logs from one `AddTelemetry` call
- Stable instrumentations: ASP.NET Core, HttpClient, Runtime, Npgsql (on by default); SqlClient opt-in
- Framework meters: Hosting, Kestrel, Routing, Diagnostics, Auth, MemoryPool, Http, DNS
- Health/metrics path filter (`/health`, `/alive`, `/ready`, `/metrics`)
- `RecordException` and `SetErrorStatusOnException`
- `telemetry.component` span tag for filtering
- Startup summary log: service name, environment, signals, exporter modes, instrumentation — plus a warning when no exporter is configured
- Source-only: Mediator (`IntegrateMediator`), MCP (`IntegrateMcp`), EventBus, MassTransit
- Exporters: OTLP, Console, optional Azure Monitor
- ParentBased sampling; AlwaysOn in Development unless `TracesSamplerRatio` is set
- Trace-based exemplars; `deployment.environment` / `service.environment` resource attributes
- `TelemetryActivity` for manual spans
- `TelemetryBuilder` hooks: `ConfigureTracing` / `ConfigureMetrics` / `ConfigureLogging` / `AddSource` / `AddMeter`

## Startup summary

One `Information` log after host start, e.g.:

```text
BuildingBlocks.Telemetry ready for catalog-api in Development. Signals: traces, metrics, logs.
Exporters: OTLP (environment). Instrumentation: aspnetcore, httpclient, runtime, npgsql, framework-meters, mediator.
```

Endpoints, OTLP headers, and connection strings are never logged. Silence it with a log filter on
`BuildingBlocks.Telemetry.Internal.Pipeline.TelemetryStartupSummaryReporter`. If signals are on but SigNoz is empty, the usual cause is **no OTLP endpoint**.

## Docs

- [Telemetry guide](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/telemetry.md)
- [CHANGELOG](https://github.com/Maxofpower/FeatureFusion/blob/main/CHANGELOG.md)

## License

MIT — Copyright (c) 2026 Mohammad Hasan Hosseini
