# BuildingBlocks.Telemetry

Config-driven OpenTelemetry for .NET hosts (production path). Backend-agnostic **OTLP** first; optional Azure Monitor. Requires `IHostApplicationBuilder`.

See the [package README](../../src/BuildingBlocks/Telemetry/PACKAGE_README.md). Local Aspire SigNoz is a **separate** package (`BuildingBlocks.Aspire.Hosting.SigNoz`) — this library only exports OTLP.

Libraries **emit** (`UseTelemetry()` on Mediator / MCP). This host **exports** those sources when `IntegrateMediator` / `IntegrateMcp` is enabled. One without the other means silent spans or missing metrics in the backend.

## Support matrix

| | |
|--|--|
| **TFMs** | net8.0, net9.0, net10.0 |
| **OTLP** | `OTEL_EXPORTER_OTLP_ENDPOINT` / `PROTOCOL` / `HEADERS`, or `Telemetry:Exporters:Otlp` |
| **Stable (default on)** | ASP.NET Core, HttpClient, Runtime, Npgsql, framework meters |
| **Opt-in (default off)** | SqlClient, MassTransit/EventBus sources, `IntegrateMcp` |
| **Exporters** | OTLP, Console, optional Azure Monitor |
| **OpenTelemetry packages** | 1.18.0 (stable only) |

EF Core, Redis, gRPC client, and Prometheus scrape are not first-class. Use `ConfigureTracing` / `ConfigureMetrics` if you need them (contrib packages, while `IServiceCollection` is still open).

## Install

```bash
dotnet add package BuildingBlocks.Telemetry
```

---

## Registration approaches

### 1. `AddTelemetry` — API or worker

```csharp
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true;        // default true — ActivitySource + Meter
    o.IntegrateMcp = true;             // default false
    o.Instrumentation.Npgsql = true;   // default true
    o.Instrumentation.EventBus = true; // default false
});
```

Options and fluent hooks in one call:

```csharp
builder.AddTelemetry(
    configureOptions: o => o.Instrumentation.EventBus = true,
    configureBuilder: t => t
        .AddSource("DbMigrations")
        .ConfigureTracing(tr => tr
            .AddEntityFrameworkCoreInstrumentation()
            .AddRedisInstrumentation()));
```

Do **not** call `AddTelemetry` twice on the same host.

### 2. Aspire `AddServiceDefaults` (FeatureFusion lab)

FeatureFusion ServiceDefaults already calls `AddTelemetry`. Pass options and hooks there:

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

Also `cfg.UseTelemetry()` on Mediator and `o.UseTelemetry()` on the MCP builder so those libraries actually create spans/meters.

### 3. `appsettings` / environment

The `Telemetry` section binds to `TelemetryOptions`. Nested keys work as `Telemetry__IntegrateMcp=true`. `ConfigureAspNetCore` / `ConfigureHttpClient` / `ConfigureSqlClient` are **code-only** (not JSON).

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
      "Otlp": { "Enabled": false, "Protocol": "Grpc" },
      "Console": { "Enabled": false }
    }
  }
}
```

Pipeline registration snapshots options at `AddTelemetry` time. `IOptions` validation-on-start does not reconfigure exporters after the host is built.

---

## OTLP — prefer env over hard-coded URLs

OTLP is registered when any of these is true:

- `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire AppHost `WithSigNozOtlpExporter` does this)
- `Telemetry:Exporters:Otlp:Enabled` is true
- `Telemetry:Exporters:Otlp:Endpoint` is a non-empty absolute URI

Prefer **env** so the same binary works locally, in CI, and against a production collector.

| Variable | Role |
|----------|------|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Collector URL (e.g. `http://localhost:4317`) |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` or `http/protobuf` |
| `OTEL_EXPORTER_OTLP_HEADERS` | Optional `key=value` pairs (ingestion keys) |

When only the env endpoint is set (no Console, no explicit options Endpoint/Headers), the host uses `UseOtlpExporter()` for traces, metrics, and logs. On that **fast-path**, `Exporters.Otlp.Protocol` in code/JSON is **ignored** — set `OTEL_EXPORTER_OTLP_PROTOCOL`.

Explicit `Endpoint` / `Headers` or Console exporter forces per-signal `AddOtlpExporter`. Those two registration styles must not be mixed.

Azure Monitor: `APPLICATIONINSIGHTS_CONNECTION_STRING` or `Exporters.AzureMonitor` (can coexist with OTLP). Console is local debug only.

---

## Enrich / Filter / extra sources

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

Order: Filter / `RecordException` → user callback → `telemetry.component`. Health paths `/health`, `/alive`, `/ready`, `/metrics` are skipped by default (`FilterHealthCheckRequests`).

Manual spans — register the source first:

```csharp
telemetry.AddSource("MyApp");
using var activity = TelemetryActivity.Start("MyApp", "Checkout");
activity?.SetTag("order.id", id);
```

Sampling: AlwaysOn in Development unless `TracesSamplerRatio` is set (0.0–1.0, wrapped in ParentBased). Set the ratio in production if you need head sampling.

---

## Startup summary

After the host starts, one `Information` log states the resolved service name, environment, enabled signals,
exporter modes (`OTLP (environment)`, `OTLP (explicit options)`, `Console`, `Azure Monitor`), and enabled
instrumentation. A `Warning` follows when signals are on but no exporter is configured — the usual cause of
"telemetry is running but SigNoz is empty". Endpoints, OTLP headers, and connection strings are never logged.
Filter the category `BuildingBlocks.Telemetry.Internal.Pipeline.TelemetryStartupSummaryReporter` to silence it.

## Mediator / MCP / EventBus split

| Library | Emits | Host opt-in |
|---------|-------|-------------|
| `BuildingBlocks.Mediator` | `ActivitySource` + Meter + `ILogger` via `UseTelemetry()` | `IntegrateMediator` (`AddSource` + `AddMeter`) |
| `BuildingBlocks.Mcp` | `ActivitySource` via `UseTelemetry()` on the MCP builder | `IntegrateMcp` (`AddSource`, opt-in) |
| EventBusRabbitMQ | `ActivitySource("EventBus")` | `Instrumentation.EventBus` |
| SqlClient | Contrib `AddSqlClientInstrumentation` | `Instrumentation.SqlClient` + optional `ConfigureSqlClient` |

Filter traces with `telemetry.component` (`mediator`, `mcp`, `npgsql`, `aspnetcore`, …). Constants: `TelemetryDefaults.*`, `TelemetryComponentTags.*`.

## Configuration (`Telemetry` section)

| Key | Default | Meaning |
|-----|---------|---------|
| `EnableTracing` / `EnableMetrics` / `EnableLogging` | `true` | Pillar toggles |
| `IntegrateMediator` | `true` | Source + meter `BuildingBlocks.Mediator` |
| `IntegrateMcp` | `false` | Source `BuildingBlocks.Mcp` |
| `Instrumentation.AspNetCore` | `true` | HTTP traces + metrics |
| `Instrumentation.HttpClient` / `Runtime` / `Npgsql` | `true` | Stable |
| `Instrumentation.IncludeFrameworkMeters` | `true` | Hosting, Kestrel, Routing, Diagnostics, Auth, MemoryPool, Http, DNS |
| `Instrumentation.FilterHealthCheckRequests` | `true` | Skip `/health`, `/alive`, `/ready`, `/metrics` |
| `Instrumentation.SqlClient` | `false` | Opt-in traces + metrics |
| `Instrumentation.MassTransit` / `EventBus` | `false` | Source-only |
| `Exporters.Otlp` | off unless env set | OTLP |
| `TracesSamplerRatio` | null | Optional ParentBased ratio |
| `AlwaysOnSamplerInDevelopment` | `true` | AlwaysOn in Development |

`Configure*` callbacks are code-only (not bindable from JSON).

## Package layout (maintainers)

Vertical slices under `Internal/`: `Pipeline/`, `Exporters/`, `Instrumentations/` (one apply type per instrumentation), plus `Options/Exporters/` for public exporter option types. Public namespaces stay flat (`BuildingBlocks.Telemetry`).

## SigNoz click-through (local Aspire)

![SigNoz ASP .NET Core Metrics for FeatureFusion in Development after catalog load.](../medium/images/07-signoz-dashboard.png)

![SigNoz metrics explorer: HTTP server duration, mediator.send.duration, Npgsql connection create time.](../medium/images/07b-signoz-metrics-explorer.png)

Crops from a Development session for service `FeatureFusion` after catalog traffic — not an SLA.

AppHost: `AddSigNoz().WithUi().WithDashboards()` + `WithSigNozOtlpExporter`. FeatureFusion AppHost uses `WithUiFromConfiguration` (`src/Lab/FeatureFusion.AppHost/appsettings.Development.json`): **`dev@local.test` / `DevPassword123!`**. Override with `SigNoz__AdminEmail`, `SigNoz__AdminPassword`, `SigNoz__UiPort`, …. The UI still requires login; root-user env only skips signup. Custom lab credentials are not shown on the Aspire connection panel. Omits `WithDataVolume()` (writable layer still persists when `Lifetime = Persistent`; sqlite volume always persists). After traffic: **Services** → **Traces** (waterfall) → **Metrics** / **Logs**. Filter spans with `telemetry.component`. Dashboard tiles use SigNoz **View in Traces**.

The seeded **BuildingBlocks Telemetry** dashboard has four sections — Service RED, Components (grouped by
`telemetry.component`), Runtime (both `dotnet.*` and `process.runtime.dotnet.*` metric names, so panels work on
net8 through net10), and Logs — with `service.name`, `deployment.environment`, and `telemetry.component` variables.
`WithDashboards()` replaces a previously seeded dashboard when its layout sections no longer match the packaged definition.

The UI waits for `signoz-schema-migrator` to exit 0 before it starts, because the query-service reads ClickHouse
directly. Starting it earlier makes Instrumentation and Traces fail with ClickHouse `code: 60 Unknown table
expression identifier` (for example `signoz_metadata.distributed_column_evolution_metadata`) until `migrate` finishes.

P95/P99 histogram tiles need SigNoz's `histogramQuantile` ClickHouse UDF. `AddSigNoz` installs it via a Session init
container before ClickHouse starts, using the official CSV stdin contract. Without that UDF, those panels show
“Something went wrong on our end” while count/rate tiles still render. A Persistent ClickHouse created with an older
UDF XML must be recreated (or restarted after the bind-mounted `histogram_function.xml` is updated) so P95/P99 stop
failing with ClickHouse `code: 754` / child process exit 2.

## Related

- [BuildingBlocks.Aspire.Hosting.SigNoz](../../src/BuildingBlocks/Aspire.Hosting.SigNoz/PACKAGE_README.md)
- [deploy/signoz/alerts](../../deploy/signoz/alerts/README.md)
- [MCP guide](mcp.md) (`IntegrateMcp`)
- [Mediator](mediator.md) (`UseTelemetry` + `IntegrateMediator`)
