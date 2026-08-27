# Changelog

All notable changes to **BuildingBlocks** packages in this repository are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## BuildingBlocks.Mediator [1.1.0] - 2026-08-27

### Added

- `ICommandPipelineBehavior<,>` / `IQueryPipelineBehavior<,>` — compile-time command vs query pipelines (MS.DI does not construct the opposite kind)
- `AddOpenCommandBehavior` / `AddOpenQueryBehavior` — fail fast when the type is not constrained
- Opt-in Send metrics on `UseTelemetry()`: histogram `mediator.send.duration` (ms), counter `mediator.send` (tags `mediator.success`, `mediator.message_kind`, `mediator.request_name`); `EnableMetrics` (default true), `MeterName` (defaults to `ActivitySourceName`)

### Changed

- NuGet README rewritten for usage (quick start, typed behaviors, telemetry)
- 1.0.1 `CommandPipelineBehavior` / `QueryPipelineBehavior` unchanged (not obsolete)

### Notes

- Drop-in upgrade from 1.0.1. No Publish / `INotification`. Host OpenTelemetry must `AddMeter("BuildingBlocks.Mediator")` (BuildingBlocks.Telemetry `IntegrateMediator` does this from 1.0.1).

## BuildingBlocks.Telemetry [1.0.1] - 2026-08-27

### Added

- `TelemetryDefaults.MediatorMeter` (same name as `MediatorActivitySource`)
- `IntegrateMediator` also `AddMeter` so mediator Send metrics export with traces

## BuildingBlocks.Telemetry [1.0.0] - 2026-08-25

### Added

- Config-driven OpenTelemetry (`AddTelemetry`) for traces, metrics, and logs
- Stable instrumentations: ASP.NET Core, HttpClient, Runtime, Npgsql, opt-in SqlClient
- Source-only ActivitySources: Mediator (`IntegrateMediator`), EventBus, MassTransit
- `telemetry.component` span tag (Enrich hooks + ActivitySource processor)
- Startup summary log (service name, environment, signals, exporter modes, instrumentation) with a warning when no exporter is configured; endpoints, headers, and connection strings are never logged
- Exporters: OTLP (`UseOtlpExporter` fast-path when env-only), Console, optional Azure Monitor
- `TelemetryActivity` helpers (`Start` / `AddEvent` / `RecordException`)
- `TelemetryBuilder` escape hatch (`ConfigureTracing` / `ConfigureMetrics` / `AddSource`)
- ParentBased + Development AlwaysOn sampling, `RecordException`, `SetErrorStatusOnException`, trace-based exemplars, `deployment.environment`
- TFM-aligned `Npgsql.OpenTelemetry` (8 / 9 / 10)

### Notes

- No prerelease OpenTelemetry dependencies. EF Core, Redis, gRPC client, and Prometheus scrape are not first-class; add the contrib package and `ConfigureTracing` if needed.
- `Exporters.Otlp.Protocol` is ignored on the OTLP fast-path (`OTEL_EXPORTER_OTLP_PROTOCOL` applies).

## BuildingBlocks.Aspire.Hosting.SigNoz [1.0.0] - 2026-08-25

### Added

- `AddSigNoz()` Aspire hosting integration (ClickHouse, ZooKeeper, schema migrator, OTLP collector, UI)
- `WithUi` credentials + SigNoz password policy (≥12 chars, upper/lower/digit/symbol)
- `WithDashboards()` seeds ASP.NET Core + BuildingBlocks dashboards (SigNoz v2 list API)
- BuildingBlocks Telemetry dashboard with Service RED, per-`telemetry.component`, runtime (net8 and net9+ metric names), and log sections, plus `service.name` / `deployment.environment` / `telemetry.component` variables
- Dashboard seeding matches on `spec.display.name` (not the generated resource slug) and replaces stale copies when packaged layout sections are missing
- `WithDataVolume` / `WithDataBindMount` persist ClickHouse **and** ZooKeeper
- `WithSigNozOtlpExporter` on `ProjectResource` only (`OTEL_EXPORTER_OTLP_*`, not `WithReference`)
- Sqlite volume keyed by `adminEmail`; default org name `default`
- Pinned images (UI v0.136.1, collector v0.144.6, ClickHouse 25.12.5); `ExcludeFromManifest`
- Query UI waits for schema-migrator completion, so Instrumentation / Traces cannot query ClickHouse before `migrate` created `signoz_traces` / `signoz_metadata` tables
- ClickHouse installs SigNoz's `histogramQuantile` UDF (Session init + XML mounts, CSV stdin matching the official binary) so dashboard P95/P99 histogram tiles do not 500 on stock `clickhouse/clickhouse-server`

### Notes

- Local-dev only. Default `Lifetime = Persistent` plus sqlite volume is not a wipe each run.
- FeatureFusion AppHost omits `WithDataVolume()`.
- The UI shows **Waiting** until the migrator exits 0 (seconds on a warm stack, longer on a first image pull).
- Docker **images** always remain after a pull. Persistent **containers** (UI, collector, ClickHouse, ZooKeeper) keep their writable layer after AppHost exit; only the schema-migrator and histogram UDF init are Session. Wipe with `Lifetime = Session` or by deleting those containers/volumes.

## BuildingBlocks.Mediator [1.0.1] - 2026-08-10

### Changed

- Multi-target **`net8.0`**, **`net9.0`**, and **`net10.0`** (was `net10.0` only)
- Package description no longer mentions a single TFM; Microsoft.Extensions.* references pinned to **8.0.x** so net8/net9 hosts are not forced onto Extensions 10
- Unit tests multi-target **`net8.0` / `net9.0` / `net10.0`**; CI installs all three SDKs and runs the full suite per TFM

## BuildingBlocks.Mediator [1.0.0] - 2026-08-08

### Added

- CQRS Send + ordered pipeline (`ISender`, `ICommand` / `IQuery`, handlers, `IPipelineBehavior`)
- Built-in handler assembly scanner (no Scrutor)
- `AddOpenBehavior(Type, int order)` and closed `AddBehavior` ordering
- `CommandPipelineBehavior` / `QueryPipelineBehavior` / `MessageKind` helpers
- `UseTelemetry` (optional ActivitySource around Send — wraps pipeline + handler; not a pipeline behavior)
- `ValidateOnStartup` handler completeness checks
- Configurable `HandlerLifetime` for discovered handlers (default Transient)
- Runtime exact-one handler resolution (clear error on missing or ambiguous handlers)
- Open-generic handler support (on-demand closing; Transient via ActivatorUtilities)
- Roslyn analyzers (BBM001 / BBM002) packed into the NuGet
- Package README, SourceLink, symbols (`snupkg`), MIT license

### Notes

- Native AOT is not fully supported (runtime `MakeGenericType` wrappers).
- Not designed to replace other mediator or messaging packages — for manual control over design patterns.
- Open-generic handlers ignore `HandlerLifetime` and always resolve as Transient.