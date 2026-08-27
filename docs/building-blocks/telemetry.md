# BuildingBlocks.Telemetry

Config-driven OpenTelemetry for .NET hosts (production path). Backend-agnostic **OTLP** first; optional Azure Monitor.

## Support matrix

| | |
|--|--|
| **TFMs** | net8.0, net9.0, net10.0 |
| **OTLP** | `OTEL_EXPORTER_OTLP_ENDPOINT` / `PROTOCOL` / `HEADERS`, or `Telemetry:Exporters:Otlp` |
| **Stable (default on)** | ASP.NET Core, HttpClient, Runtime, Npgsql, framework meters |
| **Opt-in (default off)** | SqlClient, MassTransit/EventBus sources |
| **Exporters** | OTLP, Console, optional Azure Monitor |
| **OpenTelemetry packages** | 1.18.0 (stable only) |

EF Core, Redis, gRPC client, and Prometheus scrape are not first-class. Use `ConfigureTracing` if you need them.

## Install

```bash
dotnet add package BuildingBlocks.Telemetry
```

## Usage

```csharp
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true;
    o.Instrumentation.Npgsql = true;
    o.Instrumentation.EventBus = true;
});
```

### Advanced Enrich / Filter

```csharp
builder.AddTelemetry(o =>
{
    o.Instrumentation.ConfigureAspNetCore = opts =>
    {
        opts.EnrichWithHttpRequest = (activity, request) =>
            activity.SetTag("http.route", request.Path);
    };
});
```

Also: `ConfigureHttpClient`, `ConfigureSqlClient`. Order: Filter / `RecordException` → user callback → `telemetry.component`.

Or with Aspire ServiceDefaults:

```csharp
builder.AddServiceDefaults(
    configureOptions: o =>
    {
        o.IntegrateMediator = true;
        o.Instrumentation.Npgsql = true;
        o.Instrumentation.EventBus = true;
    },
    configureTelemetry: t => t.AddSource("DbMigrations"));
```

Manual spans:

```csharp
telemetry.AddSource("MyApp");
using var activity = TelemetryActivity.Start("MyApp", "Checkout");
activity?.SetTag("order.id", id);
```

On the OTLP `UseOtlpExporter()` fast-path, `Exporters.Otlp.Protocol` is ignored; set `OTEL_EXPORTER_OTLP_PROTOCOL`.

## Startup summary

After the host starts, one `Information` log states the resolved service name, environment, enabled signals,
exporter modes (`OTLP (environment)`, `OTLP (explicit options)`, `Console`, `Azure Monitor`), and enabled
instrumentation. A `Warning` follows when signals are on but no exporter is configured — the usual cause of
"telemetry is running but SigNoz is empty". Endpoints, OTLP headers, and connection strings are never logged.
Filter the category `BuildingBlocks.Telemetry.Internal.Pipeline.TelemetryStartupSummaryReporter` to silence it.

## Mediator / EventBus split

| Library | Emits | Host opt-in |
|---------|-------|-------------|
| `BuildingBlocks.Mediator` | `ActivitySource` + Meter + `ILogger` via `UseTelemetry()` | `IntegrateMediator` (`AddSource` + `AddMeter`) |
| EventBusRabbitMQ | `ActivitySource("EventBus")` | `Instrumentation.EventBus` |
| SqlClient | Contrib `AddSqlClientInstrumentation` | `Instrumentation.SqlClient` + optional `ConfigureSqlClient` |

## Configuration (`Telemetry` section)

| Key | Default | Meaning |
|-----|---------|---------|
| `EnableTracing` / `EnableMetrics` / `EnableLogging` | `true` | Pillar toggles |
| `IntegrateMediator` | `true` | Source + meter `BuildingBlocks.Mediator` |
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
