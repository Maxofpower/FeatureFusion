# Changelog

All notable changes to **BuildingBlocks** packages in this repository are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## BuildingBlocks.Idempotency [1.0.0] - 2026-09-04

### Added

- ASP.NET Core HTTP **Idempotency-Key** filter via shared `IdempotencyGate`: MVC `[Idempotent]` / `IdempotentAttributeFilter` and Minimal API `WithIdempotency` / `IdempotentEndpointFilter`
- Response envelope replay on Completed keys: status code, content-type, body, plus configurable replay header (default `X-Idempotent-Response`)
- Cache **all HTTP 2xx** successes (not only 200)
- Processing state returns ProblemDetails (default 409) while the lease is active; expired Processing is treated as a miss
- Separate `ProcessingTtl` / `EntryTtl` with optional per-endpoint seconds on attribute / `WithIdempotency`
- Optional Redis SET NX lock via fluent `.UseRedisLock()` / `AddRedisIdempotencyLock` when `UseLock` is true (lock covers GetOrCreate)
- Opt-in request fingerprinting (`EnableRequestFingerprint`): `SHA-256(method + "\n" + path + "\n" + body)`; mismatch → ProblemDetails (default 422); default off (Exp 3 compatible)
- `DuplicateCompletedBehavior` (Replay default / Conflict), configurable processing/duplicate/fingerprint status codes, `MaxKeyLength` + control-character validation
- RFC 9457 `ProblemDetails` errors with stable `type` URIs under `https://buildingblocks.dev/errors/idempotency/`
- Optional ActivitySource via `.UseTelemetry()` / `AddIdempotencyTelemetry` (no cache key tag by default; no BuildingBlocks.Telemetry package dependency)
- Fluent DI: `AddBuildingBlocksIdempotency(...).UseRedisLock().UseTelemetry()`
- Multi-target `net8.0` / `net9.0` / `net10.0`; Lab FeatureFusion hosts MVC on `POST /api/v2/Order/order` and Minimal API smoke on `POST /api/v2/idempotency-smoke` (provenance Exp 3 / 4 / 12)

### Notes

- Distinct from MCP write idempotency (`UseMemoryIdempotency` / `IMcpIdempotencyStore` in BuildingBlocks.Mcp)
- Package depends on `Newtonsoft.Json` (MVC ObjectResult capture), `Ulid`, and `StackExchange.Redis` (Redis lock); hosts that use the lock must register `IConnectionMultiplexer`

## BuildingBlocks.Pagination.EntityFrameworkCore [1.0.0] - 2026-08-29

### Added

- One nupkg: `SortKey` / `SortKeyRegistry`, opaque HMAC-optional cursors, `ToCursorPageAsync` / `ToCursorPageMappedAsync`; IR DLL bundled (no extra Pagination package)
- EF Core source layout: `Extensions/`, `Query/Internal/`, `Infrastructure/Internal/`
- `HasKeysetIndex` — optional model helper to create the composite index that matches a `SortKey` (paging does not require it)
- Lab: FeatureFusion PostgreSQL catalog uses the EF adapter (`AsNoTracking` + `TagWith("products.list")`, SQL `Select` to `ProductDto`) on `GET /api/v2/products-page` (Minimal API; POST kept) and MVC `POST /api/v2/Product/products`; Dapper remains in-repo at `POST /api/v2/Product/products-dapper` (not packed); MCP `products.list` is the same `GetProductsQuery`
- Sort-slot CLR allowlist (`UnsupportedSortType`); `PaginationOptions.Default` is a new instance; bool/enum seek via underlying numeric type; nested value-object scalars; last page via empty cursor + `PageDirection.Backward`
- Source-generated cursor JSON; enum slots encode as `"enum"`; DateTime cursor values are UTC
- SQLite SQL probe (indexed): 10M skip 5M keyset 17.8 ms vs OFFSET 154.9 ms vs MR 1.5.0 19.9 ms; 100M skip 50M keyset 177.2 ms vs OFFSET 2470.4 ms vs MR 218.0 ms (`--probe` Stopwatch, persist with `PAGINATION_PROBE_DB`)
- Optional `PaginationOptions.Hint` (default `None`); `QueryHint.ReadUncommitted` is SQL Server session isolation (not `WITH (NOLOCK)`); EF: one transaction isolation around COUNT+PAGE when there is no ambient transaction, then restore `READ COMMITTED` on the still-open connection; ambient EF transactions are ignored (no nest); Dapper: SQL Server `SET TRANSACTION ISOLATION LEVEL` prefix, then the same restore
- Nullable value-type sort slots rejected (`NullableSortUnsupported`); `SortKeyRegistry.Get` miss is `InvalidOperationException`; slot decode failures wrap as `InvalidCursor`
- `ThenByUniqueShadowDescending`; document HMAC for untrusted HTTP, host `OrderBy` replacement, Guid vs SQL Server, no `IEnumerable`
- `ToCursorPageAsync` / `QueryCursorPageAsync` return `ValueTask`; cursor codec uses `Span`/`stackalloc` (no `Split` / `Replace` on the hot path)

## BuildingBlocks.Mcp [1.0.0] - 2026-08-28

### Added

- Message types as MCP tools on the official C# SDK (`ModelContextProtocol` 2.2.0): `[McpTool]` on types or public static endpoint methods, deny-by-default scanner, `McpResult` / `McpError`, HTTP (`MapBuildingBlocksMcp`) + opt-in stdio
- `MapTool` for dedicated MCP DTOs; overload with scoped `IServiceProvider`
- `UseMemoryIdempotency` on `McpBuilder`; write idempotency for Command/POST/PUT only (queries never use the store)
- Idempotent tool schema: `idempotencyKey` is `string` + `format: uuid` (hint for agents; invoke accepts any non-empty string)
- `WithMcp()` on Minimal API `RouteHandlerBuilder` (MVC controllers unsupported for now)
- Idempotency: keys namespaced by tool, per-key lock, optional TTL on `MemoryIdempotencyStore`, cached JSON replayed as `JsonElement`; `IMcpIdempotencyStore` for Redis in farms
- Tool `inputSchema`: CLR defaults / nullable = optional, JSON Schema `enum` (string names or integer values), `[Description]` / Swagger parameter text
- Successful `CallTool` results include JSON text **and** `structuredContent` (errors stay text + `isError`)
- Safe-tool conventions: `idempotencyKey` on writes, `IMcpToolFilter`, duck-typed Result mapping, `McpPage<T>`, dry-run / confirm / timeout, `IMcpRateLimiter`, `catalog://tools`
- Analyzers BBMCP001–005 packed in the NuGet
- Opt-in ActivitySource `BuildingBlocks.Mcp`

## BuildingBlocks.Telemetry [1.0.2] - 2026-08-28

### Added

- `IntegrateMcp` (default **false**), `TelemetryDefaults.McpActivitySource`, `TelemetryComponentTags.Mcp`

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