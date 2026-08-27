<div align="center">

# FeatureFusion

**BuildingBlocks for .NET** — CQRS Send + pipeline, config-driven OpenTelemetry, and a local Aspire SigNoz stack — plus a runnable lab that uses them.

Formerly [FeatureManagement](https://github.com/Maxofpower/FeatureManagement) (GitHub redirects).

[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Aspire](https://img.shields.io/badge/Aspire-13.4-C3002F?logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/aspire/)
[![NuGet · BuildingBlocks.Mediator](https://img.shields.io/nuget/v/BuildingBlocks.Mediator.svg?label=NuGet%20·%20Mediator&logo=nuget)](https://www.nuget.org/packages/BuildingBlocks.Mediator)
[![NuGet · BuildingBlocks.Telemetry](https://img.shields.io/nuget/v/BuildingBlocks.Telemetry.svg?label=NuGet%20·%20Telemetry&logo=nuget)](https://www.nuget.org/packages/BuildingBlocks.Telemetry)
[![NuGet · BuildingBlocks.Aspire.Hosting.SigNoz](https://img.shields.io/nuget/v/BuildingBlocks.Aspire.Hosting.SigNoz.svg?label=NuGet%20·%20SigNoz%20hosting&logo=nuget)](https://www.nuget.org/packages/BuildingBlocks.Aspire.Hosting.SigNoz)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)
[![Stars](https://img.shields.io/github/stars/Maxofpower/FeatureFusion?style=social)](https://github.com/Maxofpower/FeatureFusion/stargazers)
[![Last commit](https://img.shields.io/github/last-commit/Maxofpower/FeatureFusion)](https://github.com/Maxofpower/FeatureFusion/commits)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-mhhoseini-0A66C2?logo=linkedin&logoColor=white)](https://www.linkedin.com/in/mhhoseini/)
[![Repo](https://img.shields.io/badge/GitHub-FeatureFusion-181717?logo=github)](https://github.com/Maxofpower/FeatureFusion)

[Author · Mohammad Hasan Hosseini](https://www.linkedin.com/in/mhhoseini/) · Technical Team Lead & .NET enthusiast

</div>

---

## Table of contents

- [BuildingBlocks](#buildingblocks)
  - [How they work together](#how-they-work-together)
  - [BuildingBlocks.Mediator](#buildingblocksmediator)
  - [BuildingBlocks.Telemetry](#buildingblockstelemetry)
  - [BuildingBlocks.Aspire.Hosting.SigNoz](#buildingblocksaspirehostingsignoz)
- [Lab](#lab)
- [Architecture](#architecture)
- [Stack](#stack)
- [Repository layout](#repository-layout)
- [Prerequisites](#prerequisites)
- [Run the lab](#run-the-lab)
- [Lab features](#lab-features)
- [Design patterns](#design-patterns)
- [LinkedIn catalog](#linkedin-catalog)
- [What's next](#whats-next)
- [Testing](#testing)
- [Contributing](#contributing)

---

## BuildingBlocks

Three NuGet packages you can install in **your** hosts. The FeatureFusion API is a showcase, not a required dependency.

| Package | Role | TFMs |
|---------|------|------|
| **[BuildingBlocks.Mediator](https://www.nuget.org/packages/BuildingBlocks.Mediator)** | CQRS **Send** + ordered pipeline (`ICommand` / `IQuery`, typed behaviors, opt-in traces + metrics) | net8 / net9 / net10 |
| **[BuildingBlocks.Telemetry](https://www.nuget.org/packages/BuildingBlocks.Telemetry)** | Config-driven OpenTelemetry (traces, metrics, logs) + `IntegrateMediator` | net8 / net9 / net10 |
| **[BuildingBlocks.Aspire.Hosting.SigNoz](https://www.nuget.org/packages/BuildingBlocks.Aspire.Hosting.SigNoz)** | Local-dev Aspire `AddSigNoz()` + `WithSigNozOtlpExporter` | net10 (AppHost) |

Production apps use **Mediator + Telemetry** and export OTLP to any backend. SigNoz hosting is **local AppHost only**.

### How they work together

```mermaid
flowchart LR
  host[Your host]
  med[Mediator]
  tel[Telemetry]
  otlp[OTLP backend]
  signoz[SigNoz AppHost]
  host --> med
  med -->|"UseTelemetry"| tel
  tel -->|"AddTelemetry IntegrateMediator"| otlp
  signoz -->|"local collector"| otlp
```

1. **Mediator** dispatches commands/queries through an ordered pipeline. `UseTelemetry()` wraps **Send** (not a pipeline behavior) with an ActivitySource and Meter named `BuildingBlocks.Mediator`.
2. **Telemetry** `AddTelemetry` + `IntegrateMediator = true` registers that source **and** meter so spans and `mediator.send` metrics export with the rest of the host.
3. **SigNoz hosting** (optional, local) provisions a collector + UI. `WithSigNozOtlpExporter` sets `OTEL_EXPORTER_OTLP_*` on a **project** resource. In production, set the same env vars to your collector.

**Compose** (same shape as this lab):

```csharp
// API / worker — BuildingBlocks.Telemetry + BuildingBlocks.Mediator
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true;
    o.Instrumentation.Npgsql = true;
});

builder.Services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0); // host-owned
    cfg.UseTelemetry();
    cfg.ValidateOnStartup = true;
});

// AppHost — BuildingBlocks.Aspire.Hosting.SigNoz (local)
var signoz = builder.AddSigNoz("signoz")
    .WithUi()
    .WithDashboards();

builder.AddProject<Projects.Api>("api")
    .WithSigNozOtlpExporter(signoz);
```

---

### BuildingBlocks.Mediator

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Mediator.svg?logo=nuget)](https://www.nuget.org/packages/BuildingBlocks.Mediator)
[![Downloads](https://img.shields.io/nuget/dt/BuildingBlocks.Mediator.svg)](https://www.nuget.org/packages/BuildingBlocks.Mediator)

CQRS-first **Send** + ordered **pipeline**. Manual control over registration, pipeline order, validation, and telemetry — not a MediatR or messaging replacement (no `Publish` / `INotification` in v1).

**What's new in 1.1.0:** typed `ICommandPipelineBehavior` / `IQueryPipelineBehavior` (MS.DI does not construct the opposite kind), `AddOpenCommandBehavior` / `AddOpenQueryBehavior`, opt-in Send metrics. Drop-in from 1.0.1.

```bash
dotnet add package BuildingBlocks.Mediator
```

```csharp
public sealed record CreateOrder(string Product, int Qty) : ICommand<Guid>;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, Guid>
{
    public Task<Guid> Handle(CreateOrder command, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());
}

// Send — prefer ISender at call sites
await sender.Send(new CreateOrder("SKU-1", 2), ct);
```

| Capability | What it does |
|------------|--------------|
| `ICommand` / `ICommand<T>` / `IQuery<T>` | CQRS contracts; void writes via `ICommand : ICommand<Unit>` |
| Ordered pipeline | Open/closed behaviors; `order` lower = outermost |
| `AddOpenCommandBehavior` / `AddOpenQueryBehavior` | Command- or query-only types are not constructed for the other kind |
| `UseTelemetry()` | Optional ActivitySource + Meter around the whole Send |
| `ValidateOnStartup` | Fails fast on missing or duplicate handlers |
| Open-generic handlers | Closed on demand; built-in scanner (no Scrutor) |
| Analyzers BBM001 / BBM002 | Packed in the NuGet |

- Package README: [`src/BuildingBlocks/Mediator/PACKAGE_README.md`](src/BuildingBlocks/Mediator/PACKAGE_README.md)
- Docs: [getting-started](docs/building-blocks/getting-started.md) · [pipeline](docs/building-blocks/pipeline-behaviors.md) · [cookbook](docs/building-blocks/cookbook.md) · [test matrix](docs/building-blocks/TEST_MATRIX.md)
- Freeze / ADR: [`docs/building-blocks/mediator.md`](docs/building-blocks/mediator.md) · [`docs/adr/0001-mediator-building-blocks-in-monorepo.md`](docs/adr/0001-mediator-building-blocks-in-monorepo.md)
- [Mediator Pattern + Pipeline Behavior](https://www.linkedin.com/feed/update/urn:li:activity:7311311587372367873/) (prior post; building-blocks revision planned)

---

### BuildingBlocks.Telemetry

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Telemetry.svg?logo=nuget)](https://www.nuget.org/packages/BuildingBlocks.Telemetry)
[![GitHub Release](https://img.shields.io/github/v/release/Maxofpower/FeatureFusion?filter=telemetry-v*&logo=github&label=GitHub%20Release)](https://github.com/Maxofpower/FeatureFusion/releases/tag/telemetry-v1.0.0)

Config-driven OpenTelemetry for ASP.NET Core: traces, metrics, and logs from one `AddTelemetry` call. Export **OTLP to any backend**. Requires `IHostApplicationBuilder`.

**What's new in 1.0.1:** `IntegrateMediator` also `AddMeter("BuildingBlocks.Mediator")` so Send metrics export with traces (`TelemetryDefaults.MediatorMeter`).

```bash
dotnet add package BuildingBlocks.Telemetry
```

```csharp
builder.AddTelemetry(o =>
{
    o.IntegrateMediator = true;
    o.Instrumentation.Npgsql = true;
    o.Instrumentation.EventBus = true;
});
```

OTLP turns on when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Prefer env over hard-coded URLs.

| Capability | What it does |
|------------|--------------|
| `AddTelemetry` | Traces + metrics + logs, resource `deployment.environment` |
| ASP.NET / HttpClient / Runtime / Npgsql | On by default; SqlClient opt-in |
| `IntegrateMediator` | ActivitySource **and** Meter for `BuildingBlocks.Mediator` |
| `telemetry.component` | Span tag for filtering in the backend |
| `TelemetryBuilder` | `ConfigureTracing` / `AddSource` / `AddMeter` for EF, Redis, extra meters |
| Startup summary | One Information log of signals and instrumentation (never logs endpoints or secrets) |

- Package README: [`src/BuildingBlocks/Telemetry/PACKAGE_README.md`](src/BuildingBlocks/Telemetry/PACKAGE_README.md)
- Docs: [telemetry](docs/building-blocks/telemetry.md)

---

### BuildingBlocks.Aspire.Hosting.SigNoz

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Aspire.Hosting.SigNoz.svg?logo=nuget)](https://www.nuget.org/packages/BuildingBlocks.Aspire.Hosting.SigNoz)
[![GitHub Release](https://img.shields.io/github/v/release/Maxofpower/FeatureFusion?filter=signoz-v*&logo=github&label=GitHub%20Release)](https://github.com/Maxofpower/FeatureFusion/releases/tag/signoz-v1.0.0)

Local-dev **Aspire AppHost** integration: ClickHouse, ZooKeeper, schema migrator, OTLP collector, and SigNoz UI. **Not for production** — production still uses `BuildingBlocks.Telemetry` against any OTLP endpoint. Docker required. TFM **net10.0** (Aspire 13.4.6).

```bash
dotnet add package BuildingBlocks.Aspire.Hosting.SigNoz
```

```csharp
var signoz = builder.AddSigNoz("signoz")
    .WithUi()
    .WithDashboards();

builder.AddProject<Projects.Api>("api")
    .WithSigNozOtlpExporter(signoz);
```

Run the AppHost **https** profile. The UI waits until the migrator exits 0.

| API | Role |
|-----|------|
| `AddSigNoz` | ZooKeeper, ClickHouse, migrator, collector, UI |
| `WithUi` | Host port + local admin credentials (password policy applies) |
| `WithDashboards` | Seeds ASP.NET Core + BuildingBlocks dashboards |
| `WithDataVolume` / `WithDataBindMount` | Persist ClickHouse **and** ZooKeeper |
| `WithSigNozOtlpExporter` | `OTEL_EXPORTER_OTLP_*` on a **`ProjectResource` only** |

- Package README: [`src/BuildingBlocks/Aspire.Hosting.SigNoz/PACKAGE_README.md`](src/BuildingBlocks/Aspire.Hosting.SigNoz/PACKAGE_README.md)
- Docs: [telemetry](docs/building-blocks/telemetry.md) · [alerts](deploy/signoz/alerts/README.md)

---

## Lab

Install the packages above in your own hosts, **or** clone this repo and run **FeatureFusion** — a showcase API + AppHost that already wires Mediator, Telemetry, and SigNoz.

| Area | What you get |
|------|----------------|
| Mediator (CQRS) | **`BuildingBlocks.Mediator`** — used by FeatureFusion handlers |
| Telemetry | **`BuildingBlocks.Telemetry`** in ServiceDefaults; **`BuildingBlocks.Aspire.Hosting.SigNoz`** on AppHost |
| Event bus | RabbitMQ + transactional outbox/inbox, DLQ, dedup hooks |
| Aspire lab | AppHost orchestration for Postgres, Redis, RabbitMQ, Memcached, SigNoz |
| IdempotentFusion | ULID `Idempotency-Key` + Redis status tracking + optional lock |
| Feature flags (demo) | ASP.NET Core Feature Management + custom filters (claims / VIP) |
| API surface | Versioned controllers + Minimal APIs, FluentValidation patterns |
| Gateway | YARP reverse proxy + Memcached distributed rate limiting |
| Caching | Redis / Memcached / memory managers + middleware demos |
| Pagination | **Generic bidirectional keyset (cursor) pagination** |
| Design patterns | Mediator, Decorator, CoR, Strategy, and more — see below |

Also in the lab: app/DB initializers, middleware dynamic caching, Aspire AppHost integration tests, and performance-minded practices (OTel hooks, resilience).

> Aspire-hosted functional tests and Compose need **Docker**.

---

## Architecture

```mermaid
flowchart LR
  Client([HTTP clients]) --> FF[FeatureFusion API]
  Client --> GW[ApiGateway]
  GW --> FF
  subgraph aspireHost [Aspire AppHost]
    FF
    PG[(Postgres)]
    RD[(Redis)]
    RMQ[[RabbitMQ]]
    MC[(Memcached)]
    SZ[SigNoz]
  end
  FF --> PG
  FF --> RD
  FF --> RMQ
  FF --> MC
  FF -->|"OTLP"| SZ
  GW --> MC
```

---

## Stack

[![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-10-512BD4?logo=dotnet)](https://learn.microsoft.com/aspnet/core/)
[![EF Core](https://img.shields.io/badge/EF_Core-Npgsql-2C8EBB)](https://www.npgsql.org/efcore/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ.Client-7-FF6600?logo=rabbitmq&logoColor=white)](https://www.rabbitmq.com/)
[![Redis](https://img.shields.io/badge/Redis-StackExchange-DC382D?logo=redis&logoColor=white)](https://stackexchange.github.io/StackExchange.Redis/)
[![YARP](https://img.shields.io/badge/YARP-Reverse_proxy-0078D4)](https://microsoft.github.io/reverse-proxy/)
[![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-enabled-000000?logo=opentelemetry&logoColor=white)](https://opentelemetry.io/)
[![xUnit](https://img.shields.io/badge/tests-xUnit-512BD4)](https://xunit.net/)
[![Docker](https://img.shields.io/badge/Docker-required-2496ED?logo=docker&logoColor=white)](https://www.docker.com/)

**.NET 10** (`net10.0`) for the lab · packages also target **net8 / net9** where noted · **Aspire 13.4.x** · FluentValidation · Feature Management · Memcached (Enyim)

---

## Repository layout

```text
FeatureFusion.sln                   # .NET only
src/                                # C# only
  BuildingBlocks/
    Mediator/                       # CQRS Send + pipeline NuGet
    Mediator.Analyzers/
    Telemetry/                      # Config-driven OpenTelemetry NuGet
    Aspire.Hosting.SigNoz/          # AddSigNoz() Aspire hosting NuGet
  Lab/
    FeatureFusion/                  # Web API showcase (Features/, Infrastructure/, Controllers, Minimal APIs)
    FeatureFusion.ApiGateway/       # YARP + Memcached rate limiter
    FeatureFusion.AppHost/          # Aspire AppHost (+ SigNoz stack)
    FeatureFusion.ServiceDefaults/
    EventBus/                       # Reusable RabbitMQ event bus (namespaces stay EventBusRabbitMQ)
web/                                # reserved — Next.js project root (not created yet; not in the .sln)
tests/
  BuildingBlocks/
    Mediator.Tests/
    Mediator.Analyzers.Tests/
    Telemetry.Tests/
    Aspire.Hosting.SigNoz.Tests/
  Lab/
    IntegrationTests/               # Aspire fixture · EventBus + HTTP API smoke
    FeatureFusion.Tests/            # Unit / filter / mediator
    FeatureFusion.ApiGateway.Tests/
    FeatureFusion.Common/
benchmarks/BuildingBlocks/Mediator.Benchmarks/
deploy/signoz/alerts/               # Repo-owned SigNoz alert samples (not packaged)
docs/
  linkedin-posts.md                 # Post ↔ code map
  building-blocks/
```

<details>
<summary>Preferred vertical-slice shape</summary>

```text
Features/{Name}/
  Commands/
  Queries/
  Behaviors/
  IntegrationEvents/
```

</details>

---

## Prerequisites

| Tool | Why |
|------|-----|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Build & run the lab (package tests also use 8 / 9 SDKs in CI) |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Linux containers, running) | Aspire resources, Compose, functional tests |
| Aspire dashboard (optional) | Resource graph when using AppHost |

If the Aspire dashboard shows **Container runtime not installed** while `docker info` works, set `DOTNET_ASPIRE_CONTAINER_RUNTIME=docker` (AppHost already sets this) and restart the IDE/terminal so PATH includes Docker CLI.

---

## Run the lab

### Option A — Aspire AppHost (recommended)

```bash
dotnet run --project src/Lab/FeatureFusion.AppHost
```

Starts Postgres, Redis, RabbitMQ, Memcached, SigNoz, and `FeatureFusion`. Open the Aspire dashboard URL printed in the console.

The SigNoz UI always shows a **login** page. Root-user env vars skip the first-run **signup** wizard; they do not disable auth. This lab’s credentials come from `src/Lab/FeatureFusion.AppHost/appsettings.Development.json` via `WithUiFromConfiguration` (override with `SigNoz__AdminEmail` / `SigNoz__AdminPassword`):

| | |
|---|---|
| Email | `dev@local.test` |
| Password | `DevPassword123!` |

Those custom credentials are **not** shown on the Aspire resource panel (only package-default `WithUi()` creds are). Package defaults, if you call `WithUi()` with no overrides, are `admin@localhost.local` / `Admin@Signoz1`. If login fails after changing email, delete the persistent SigNoz sqlite volume and restart AppHost.

### Option B — Docker Compose

```bash
docker compose up -d --build
```

Uses SDK / ASP.NET **10.0** images plus supporting services.

### Option C — API only

```bash
dotnet restore FeatureFusion.sln
dotnet run --project src/Lab/FeatureFusion --launch-profile https
```

Point connection strings in `appsettings.*.json` (or user secrets) at your local infra.

<details>
<summary>Feature-flag greeting smoke</summary>

1. `POST /api/v1/Auth/login` with `vipuser` / `vippassword`
2. `GET /api/v1/Greeting/custom-greeting` with `Authorization: Bearer <token>`

</details>

---

## Lab features

### RabbitMQ EventBus (outbox / inbox / DLQ)

Transactional outbox with optional direct publish fallback, inbox/dedup hooks, DLX, and Aspire-hosted integration tests.

**Setup:** AppHost or Compose, then `dotnet test tests/Lab/IntegrationTests`.  
**LinkedIn:** see the [catalog](docs/linkedin-posts.md).

### Distributed rate limiting (YARP + Memcached)

IP-based fixed-window limiting at the reverse proxy with Memcached-backed counters. Excess traffic receives `429 Too Many Requests`.

```bash
docker compose up -d
# point traffic at the ApiGateway (see launchSettings / appsettings)
```

### Feature management filters

Conditional features via Microsoft.FeatureManagement and custom filters (e.g. VIP claims). Versioned controllers and Minimal APIs under `/api/v1|v2/...`.

### IdempotentFusion

REST idempotency with ULID keys and Redis status tracking (`POST /api/v2/Order/order`).

- [Idempotency with CQRS](https://www.linkedin.com/feed/update/urn:li:activity:7303686809891356676/)
- [IdempotentFusion project](https://www.linkedin.com/feed/update/urn:li:activity:7309149985307029504/)

### API versioning & validation

Controllers + Minimal API groups; FluentValidation via controllers, generic endpoint filters, and `WithValidation` / `MapPostWithValidation`.

### Caching, middleware & pagination

Redis / Memcached / memory managers, feature-flagged recommendation cache middleware, and DB migration/seed initializers.

### Generic bidirectional cursor (keyset) pagination

Reusable, type-safe keyset pagination for EF Core: Base64 JSON cursors (last value + id + sort + direction), dynamic sorting, forward/back navigation, and expression-tree filters — integrated with CQRS / Mediator.

- Code: `src/Lab/FeatureFusion/Infrastructure/CursorPagination`
- Demo: product listing via `ProductService` / `PaginationHelper`
- LinkedIn: [Reusable Cursor (keyset) Pagination](https://www.linkedin.com/feed/update/urn:li:activity:7325068550614708225/)

---

## Design patterns

| Pattern | Where it shows up |
|---------|-------------------|
| **Mediator / CQRS** | `BuildingBlocks.Mediator` — `ICommand`/`IQuery` Send + pipeline; host handlers in FeatureFusion |
| **CQRS** | `Features/.../Commands` + `Queries` with dedicated handlers |
| **Void command** | `ICommand : ICommand<Unit>` — concrete type in pipeline (no Adapter / `IRequest`) |
| **Decorator** | Pipeline behaviors; EventBus handler decorators in tests |
| **Singleton** | Cached mediator wrappers / long-lived Redis multiplexer |
| **Factory** | Resilience / connection helpers; `CursorFactory`; gateway Memcached factory |
| **Repository / DbContext** | EF Core `CatalogDbContext` + feature handlers |
| **Unit of work** | `ResilientTransaction` spanning business write + outbox |
| **Strategy** | Feature filters & validation styles (controller vs Minimal API) |
| **Template method** | `BaseValidator.PostInitialize` |
| **Keyset pagination** | `Infrastructure/CursorPagination` — type-safe bidirectional cursors |
| **Chain of Responsibility** | Feature toggle rule evaluation; mediator pipeline chain |
| **Observer / messaging** | RabbitMQ integration events (outbox → bus → handlers) |
| **Outbox / Inbox** | `TransactionalOutbox` + `OutBoxWorker` |
| **Polling publisher** | `OutBoxWorker` background poll → publish |
| **Dead letter queue** | EventBus DLX / DLQ topology |
| **Message deduplication** | Inbox + `MessageDeduplicationService` |
| **Idempotency** | `IdempotentAttribute` + Redis status tracking |
| **Feature toggle** | ASP.NET Core Feature Management + custom filters |
| **Rate limiting** | ApiGateway Memcached fixed-window limiter |
| **Circuit breaker / resilience** | Polly `ResiliencePipelineFactory` |
| **Options** | `AddOptions` / `IOptions<>` for EventBus, Redis, Memcached |
| **Middleware pipeline** | `RecommendationCacheMiddleware` |
| **Cache-aside** | Memcached/Redis `GetValueOrCreateAsync` |
| **Result object** | `Result<T>` + `Match` / HTTP mapping |
| **API Gateway / reverse proxy** | YARP `FeatureFusion.ApiGateway` |
| **API versioning** | Asp.Versioning on controllers and Minimal APIs |
| **Dependency Injection** | `Program` / `BuilderExtensions` composition |

---

## LinkedIn catalog

Post ↔ code map: [`docs/linkedin-posts.md`](docs/linkedin-posts.md) · [Follow on LinkedIn](https://www.linkedin.com/in/mhhoseini/)

---

## What's next

This remains a **public .NET lab**. Near-term direction:

- More **BuildingBlocks.\*** packages extracted from the showcase
- Frontend showcase (`web/`, Next.js project root) and MCP (`BuildingBlocks.Mcp`) when they land
- Keep the LinkedIn catalog in sync when new posts ship
- Pub/sub stays a **sibling** story (not Mediator notifications)

---

## Testing

```bash
dotnet test FeatureFusion.sln -c Release
```

| Project | Notes |
|---------|--------|
| `BuildingBlocks.Mediator.Tests` | Package suite on **net8 / net9 / net10** |
| `BuildingBlocks.Mediator.Analyzers.Tests` | BBM001 / BBM002 |
| `BuildingBlocks.Telemetry.Tests` | `AddTelemetry` / `IntegrateMediator` |
| `BuildingBlocks.Aspire.Hosting.SigNoz.Tests` | AppHost integration |
| `IntegrationTests` | Shared Aspire fixture — EventBus integration **and** HTTP API smoke (`Api/FeatureFusionApiTests`) |
| `FeatureFusion.Tests` | Unit / filter / mediator (single-dependency containers where useful) |
| `FeatureFusion.ApiGateway.Tests` | Memcached-backed limiter tests |

API / functional coverage uses the **Aspire** fixture in `IntegrationTests` (dynamic ports; stop a local AppHost if you still hit conflicts).

---

## Contributing

PRs welcome. Prefer vertical-slice feature folders, XML docs on public APIs, constants over magic strings, and tests + catalog updates when behavior changes. See [CONTRIBUTING.md](CONTRIBUTING.md).

License: **MIT** — see [LICENSE.txt](LICENSE.txt).
