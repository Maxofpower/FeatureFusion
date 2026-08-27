# BuildingBlocks.Telemetry

Config-driven OpenTelemetry metrics, logging, and tracing for .NET.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Telemetry.svg)](https://www.nuget.org/packages/BuildingBlocks.Telemetry)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## What's new in 1.0.1

- `IntegrateMediator` also `AddMeter("BuildingBlocks.Mediator")` so Send metrics export with traces
- `TelemetryDefaults.MediatorMeter` (same name as the mediator ActivitySource)

## Requirements

- **.NET 8**, **.NET 9**, or **.NET 10** (`net8.0` / `net9.0` / `net10.0`)
- `IHostApplicationBuilder` (`Microsoft.AspNetCore.App`)

## Install

```bash
dotnet add package BuildingBlocks.Telemetry
```

## Quick start

```csharp
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true;
    o.Instrumentation.Npgsql = true;
    o.Instrumentation.EventBus = true;
});

builder.AddTelemetry(
    configureOptions: o => o.EnableMetrics = true,
    configureBuilder: t => t.AddSource("DbMigrations"));
```

OTLP turns on when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Prefer env over hard-coded endpoints. On that fast-path, `Exporters.Otlp.Protocol` is ignored; use `OTEL_EXPORTER_OTLP_PROTOCOL`.

## Features

- Tracing, metrics, and logs from one `AddTelemetry` call
- Stable instrumentations: ASP.NET Core, HttpClient, Runtime, Npgsql (on by default); SqlClient opt-in
- Framework meters: Hosting, Kestrel, Routing, Diagnostics, Auth, MemoryPool, Http, DNS
- Health/metrics path filter (`/health`, `/alive`, `/ready`, `/metrics`)
- `RecordException` and `SetErrorStatusOnException`
- `telemetry.component` span tag for filtering
- Startup summary log: service name, environment, signals, exporter modes, instrumentation — plus a warning when no exporter is configured
- Source-only: Mediator (`IntegrateMediator`), EventBus, MassTransit
- Exporters: OTLP, Console, optional Azure Monitor
- ParentBased sampling; AlwaysOn in Development unless `TracesSamplerRatio` is set
- Trace-based exemplars; `deployment.environment` / `service.environment` resource attributes
- `TelemetryActivity` for manual spans
- `TelemetryBuilder` hooks: `ConfigureTracing` / `ConfigureMetrics` / `ConfigureLogging` / `AddSource` / `AddMeter`

`ConfigureAspNetCore` / `ConfigureHttpClient` / `ConfigureSqlClient` order: Filter / `RecordException` → user callback → `telemetry.component`.

Extra instrumentations (not shipped here) go through `TelemetryBuilder.ConfigureTracing`.

## Startup summary

One `Information` log after host start, e.g.:

```text
BuildingBlocks.Telemetry ready for catalog-api in Development. Signals: traces, metrics, logs.
Exporters: OTLP (environment). Instrumentation: aspnetcore, httpclient, runtime, npgsql, framework-meters, mediator.
```

Endpoints, OTLP headers, and connection strings are never logged. Silence it with a log filter on
`BuildingBlocks.Telemetry.Internal.Pipeline.TelemetryStartupSummaryReporter`.

## Docs

- [Telemetry guide](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/telemetry.md)
- [CHANGELOG](https://github.com/Maxofpower/FeatureFusion/blob/main/CHANGELOG.md)

## License

MIT — Copyright (c) 2026 Mohammad Hasan Hosseini
