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

## Quick start

Defaults are enough for most hosts. Set `OTEL_EXPORTER_OTLP_ENDPOINT` for export. Do not call `AddTelemetry` twice.

```csharp
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true;
    o.IntegrateMcp = true;               // default false
    o.Instrumentation.EventBus = true;   // default false
});
```

## Quick start — all options

One `AddTelemetry` call. Values below are the **defaults** unless marked opt-in. Prefer `OTEL_EXPORTER_OTLP_*` over `Exporters.Otlp.Endpoint`. Do not call `AddTelemetry` twice (if your ServiceDefaults host already calls it, pass this callback there — `AddServiceDefaults` is not in this package).

```csharp
builder.AddTelemetry(o =>
{
    // Resource (empty ServiceName → IHostEnvironment.ApplicationName)
    o.ServiceName = null;
    o.ServiceNamespace = null;
    o.ServiceVersion = null;
    o.ResourceAttributes["team"] = "platform"; // extra; deployment.environment / service.environment always set

    // Signals (all default true)
    o.EnableTracing = true;
    o.EnableMetrics = true;
    o.EnableLogging = true;

    // Library sources: emit with UseTelemetry() on Mediator/MCP; these flags export them
    o.IntegrateMediator = true;  // default true — AddSource + AddMeter BuildingBlocks.Mediator
    o.IntegrateMcp = true;       // default false — AddSource BuildingBlocks.Mcp

    o.Sources.Add("MyApp");      // extra ActivitySources
    o.Meters.Add("MyApp");       // extra meters

    o.TracesSamplerRatio = null;           // null: AlwaysOn in Development, SDK default otherwise
    o.AlwaysOnSamplerInDevelopment = true; // ignored when TracesSamplerRatio is set
    o.SetErrorStatusOnException = true;
    o.EnableTraceBasedExemplars = true;

    var i = o.Instrumentation;
    i.AspNetCore = true;
    i.HttpClient = true;
    i.Runtime = true;
    i.Npgsql = true;                    // default true
    i.IncludeFrameworkMeters = true;    // Hosting, Kestrel, Routing, Diagnostics, Auth, MemoryPool, Http, DNS
    i.FilterHealthCheckRequests = true; // /health, /alive, /ready, /metrics
    // i.ExcludedPathPrefixes.Add("/swagger");
    i.RecordException = true;           // before Configure* callbacks

    i.SqlClient = false;                // opt-in
    i.EventBus = true;                  // opt-in — ActivitySource "EventBus"
    i.MassTransit = false;              // opt-in — ActivitySource "MassTransit"

    // Code-only (not JSON). Order: Filter / RecordException → this callback → telemetry.component
    i.ConfigureAspNetCore = opts =>
        opts.EnrichWithHttpRequest = (activity, request) => activity.SetTag("http.route", request.Path);
    i.ConfigureHttpClient = opts => { };
    i.ConfigureSqlClient = opts =>
        opts.EnrichWithSqlCommand = (activity, command) =>
            activity.SetTag("db.command_type", command.CommandType.ToString());

    // Exporters — leave Otlp.Enabled false and set OTEL_EXPORTER_OTLP_ENDPOINT for the env fast-path
    o.Exporters.Otlp.Enabled = false;
    o.Exporters.Otlp.Endpoint = null;           // if set, per-signal AddOtlpExporter (no fast-path)
    o.Exporters.Otlp.Headers = null;            // same — forces per-signal path
    o.Exporters.Otlp.Protocol = TelemetryOtlpProtocol.Grpc; // ignored on env fast-path; use OTEL_EXPORTER_OTLP_PROTOCOL
    o.Exporters.Otlp.ProtocolName = null;       // appsettings "grpc" | "http/protobuf" wins over Protocol
    o.Exporters.Console.Enabled = false;        // local debug; also disables OTLP fast-path
    o.Exporters.AzureMonitor.Enabled = false;
    o.Exporters.AzureMonitor.ConnectionString = null; // or APPLICATIONINSIGHTS_CONNECTION_STRING
},
configureBuilder: t =>
{
    t.AddSource("DbMigrations");
    t.AddMeter("DbMigrations");
    t.ConfigureResource(r => { });
    t.ConfigureTracing(tr => tr
        .AddEntityFrameworkCoreInstrumentation()  // contrib — not shipped
        .AddRedisInstrumentation());
    t.ConfigureMetrics(m => { });
    t.ConfigureLogging(l => { });
});

using var activity = TelemetryActivity.Start("MyApp", "Checkout");
activity?.SetTag("order.id", id);
activity?.AddEvent("payment.started");
activity?.RecordException(ex);
```

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
# OTEL_EXPORTER_OTLP_HEADERS=signoz-ingestion-key=...
```

Env-only OTLP uses `UseOtlpExporter()` for traces, metrics, and logs. Do not mix that with explicit `Endpoint` / `Headers` / Console.

`ConfigureTracing` / `ConfigureMetrics` must run while `IServiceCollection` is still open. EF Core, Redis, gRPC client, and Prometheus scrape are **not** first-class.

Mediator: `cfg.UseTelemetry()` then `IntegrateMediator`. MCP: builder `UseTelemetry()` then `IntegrateMcp`. Without `Integrate*`, spans stay in-process.

## Configuration section (bindable)

`Telemetry` JSON / `Telemetry__*`. `ConfigureAspNetCore` / `ConfigureHttpClient` / `ConfigureSqlClient` are **code-only**.

```json
{
  "Telemetry": {
    "ServiceName": null,
    "ServiceNamespace": null,
    "ServiceVersion": null,
    "EnableTracing": true,
    "EnableMetrics": true,
    "EnableLogging": true,
    "IntegrateMediator": true,
    "IntegrateMcp": false,
    "Sources": [ "MyApp" ],
    "Meters": [ "MyApp" ],
    "TracesSamplerRatio": null,
    "AlwaysOnSamplerInDevelopment": true,
    "SetErrorStatusOnException": true,
    "EnableTraceBasedExemplars": true,
    "Instrumentation": {
      "AspNetCore": true,
      "HttpClient": true,
      "Runtime": true,
      "Npgsql": true,
      "IncludeFrameworkMeters": true,
      "FilterHealthCheckRequests": true,
      "ExcludedPathPrefixes": [ "/health", "/alive", "/ready", "/metrics" ],
      "RecordException": true,
      "SqlClient": false,
      "EventBus": false,
      "MassTransit": false
    },
    "Exporters": {
      "Otlp": {
        "Enabled": false,
        "Endpoint": null,
        "Protocol": "Grpc",
        "ProtocolName": null,
        "Headers": null
      },
      "Console": { "Enabled": false },
      "AzureMonitor": { "Enabled": false, "ConnectionString": null }
    }
  }
}
```

## Startup summary

One `Information` log after host start, e.g.:

```text
BuildingBlocks.Telemetry ready for catalog-api in Development. Signals: traces, metrics, logs.
Exporters: OTLP (environment). Instrumentation: aspnetcore, httpclient, runtime, npgsql, framework-meters, mediator, mcp.
```

Endpoints, OTLP headers, and connection strings are never logged. Silence it with a log filter on
`BuildingBlocks.Telemetry.Internal.Pipeline.TelemetryStartupSummaryReporter`. If signals are on but SigNoz is empty, the usual cause is **no OTLP endpoint**.

## Docs

- [Telemetry guide](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/telemetry.md)
- [CHANGELOG](https://github.com/Maxofpower/FeatureFusion/blob/main/CHANGELOG.md)

## License

MIT — Copyright (c) 2026 Mohammad Hasan Hosseini
