# BuildingBlocks.Aspire.Hosting.SigNoz

Local Aspire AppHost integration for SigNoz. Production telemetry belongs in `BuildingBlocks.Telemetry` against an OTLP backend.

**TFM:** net10.0 (Aspire 13.4.6 AppHost). Docker required.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Aspire.Hosting.SigNoz.svg)](https://www.nuget.org/packages/BuildingBlocks.Aspire.Hosting.SigNoz)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

| API | Role |
|-----|------|
| `AddSigNoz(name, port?, otlpGrpcPort?, otlpHttpPort?, jwtSecret?, configure?)` | ZooKeeper, ClickHouse, telemetrystore migrator, OTLP collector, query UI |
| `WithUi(port?, adminEmail?, adminPassword?, adminName?, orgName?)` | UI host port + local-dev admin credentials |
| `WithDataVolume()` / `WithDataBindMount()` | Persist ClickHouse **and** ZooKeeper (optional) |
| `WithDashboards()` | Seeds ASP.NET Core + BuildingBlocks dashboards after UI is healthy |
| `WithSigNozOtlpExporter()` | Sets `OTEL_EXPORTER_OTLP_*` on **`ProjectResource` only** (not `WithReference`) |

## Install

```bash
dotnet add package BuildingBlocks.Aspire.Hosting.SigNoz
```

## Image tags

Pinned in `SigNozContainerImageTags` (not `:latest`). Override with `configure: o => o.SigNozTag = "..."` or `.WithImageTag(...)` on the UI resource.

Package defaults: UI **v0.136.1**, collector/migrator **v0.144.6**, ClickHouse **25.12.5**.

## Startup order

`{name}-schema-migrator` is a one-shot Session job that runs `migrate bootstrap/sync/async` against ClickHouse. Both the collector and the query UI `WaitForCompletion` on it, so the UI cannot query a ClickHouse that is missing `signoz_traces` / `signoz_metadata` tables (which surfaces as ClickHouse `code: 60 Unknown table expression identifier` on Instrumentation and Traces).

An **Exited (0)** migrator row is expected success, and the UI stays **Waiting** until it finishes.

`{name}-clickhouse-udf` is a one-shot Session job that installs SigNoz's `histogramQuantile` ClickHouse UDF (stock ClickHouse does not ship it). The function XML uses **CSV** stdin, matching the official binary. P95/P99 histogram tiles call that function; without it the UI shows “Something went wrong on our end.” ClickHouse waits for the init job, and an **Exited (0)** UDF row is expected. If those tiles fail with ClickHouse `code: 754` (child process exit 2) on an existing Persistent ClickHouse, recreate that container so it remounts the current XML.

## Lifetime and persistence

Default `Lifetime = Persistent` plus a sqlite volume means omitting `WithDataVolume()` is **not** a wipe each run.

- Persistent containers reuse the writable layer across AppHost restarts. Docker **images** always remain after a pull; that is expected.
- Sqlite always persists (volume name hashed from `adminEmail`). Changing the password with the same email does not rotate the volume.
- True wipe: `Lifetime = Session` **or** delete Docker volumes / leftover Persistent containers.
- `WithDataVolume()` / `WithDataBindMount()` persist **both** ClickHouse (`/var/lib/clickhouse`) and ZooKeeper (`/bitnami/zookeeper`). ClickHouse-only persistence leaves replicated tables read-only after restart.

## Password

Admin password must be ≥12 characters, with upper, lower, digit, and a symbol from the SigNoz whitelist (`~!@#$%^&*` and similar). Defaults: `admin@localhost.local` / `Admin@Signoz1`.

## Quick start

```csharp
var signoz = builder.AddSigNoz("signoz")
    .WithUi()
    .WithDashboards();

builder.AddProject<Projects.Api>("api")
    .WithSigNozOtlpExporter(signoz);
```

Durable store: add `.WithDataVolume()` when you need ClickHouse/ZooKeeper history across restarts.

- **UI**: connection string is the SigNoz UI URL.
- **OTLP**: `WithSigNozOtlpExporter` on a `ProjectResource` only.
- Run AppHost with the **https** launch profile.

Span filter `telemetry.component` is applied by **BuildingBlocks.Telemetry**, not this package.

## Seeded dashboards

`WithDashboards()` seeds two dashboards, matching on `spec.display.name` (the generated `name` slug is ignored).
A previously seeded copy is replaced when its layout sections no longer match the packaged definition, and extra copies of the same title are removed.

- **ASP .NET Core Metrics** — request, process, GC, threading, routing, and memory-pool metrics.
- **BuildingBlocks Telemetry** — four sections driven by `service.name`, `deployment.environment`,
  and `telemetry.component` variables:
  - *Service RED*: request rate, P95 latency, 5xx rate, active requests, duration percentiles, rate by status code and route.
  - *Components*: span count, P95 span duration, and error spans grouped by `telemetry.component`, plus the slowest span names.
  - *Runtime*: GC heap, collections, allocation rate, thread pool, and exceptions — each panel queries both the .NET 9+ (`dotnet.*`) and .NET 8 (`process.runtime.dotnet.*`) metric names.
  - *Logs*: volume by severity and error/fatal volume.

## Docs

- [Telemetry guide](https://github.com/Maxofpower/FeatureManagement/blob/main/docs/building-blocks/telemetry.md)
- [CHANGELOG](https://github.com/Maxofpower/FeatureManagement/blob/main/CHANGELOG.md)

## License

MIT — Copyright (c) 2026 Mohammad Hasan Hosseini
