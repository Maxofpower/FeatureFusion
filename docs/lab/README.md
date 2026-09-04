# FeatureFusion Lab

The **application is the laboratory**. A NuGet BuildingBlock is a reusable result that earned a boundary through real Aspire-backed experiments — not a framework dumped into the monorepo.

This document is the lab charter. Experiment procedures live in [`tests/Lab/IntegrationTests/Experiments/README.md`](../../tests/Lab/IntegrationTests/Experiments/README.md). Package install docs live under [`docs/building-blocks/`](../building-blocks/).

## What the Lab is

| Layer | Role |
|-------|------|
| **FeatureFusion API** (`src/Lab/FeatureFusion`) | Vertical-slice host: HTTP + MCP tools share Mediator handlers |
| **Aspire AppHost** (`src/Lab/FeatureFusion.AppHost`) | Local orchestration: Postgres, Redis, RabbitMQ, Memcached, SigNoz OTLP |
| **Integration experiments** | Falsifiable hypotheses against real infrastructure (no mocks for the path under test) |
| **BuildingBlocks** (`src/BuildingBlocks/*`) | MIT NuGet packages extracted only when a contract is proven and reusable |

## What the Lab is not

- Not a Scenario/Actor/ExperimentEngine product (yet — see roadmap)
- Not a kitchen-sink framework: do not install BuildingBlocks “just because they exist”
- Not where LinkedIn URLs belong in NuGet XML / `PACKAGE_README.md` (keep those in [`docs/linkedin-posts.md`](../linkedin-posts.md))

## Architecture (current)

```
Cursor / Claude / MAF prototype (optional)
        │ MCP HTTP /mcp
        ▼
FeatureFusion (WAF in tests / AppHost in local)
  ├── BuildingBlocks.Mcp          → tools catalog + invoker
  ├── BuildingBlocks.Mediator     → Send + pipeline
  ├── BuildingBlocks.Idempotency  → HTTP Idempotency-Key (Redis)
  ├── BuildingBlocks.Pagination.* → products.list / products-page
  ├── BuildingBlocks.Telemetry    → OTel (IntegrateMediator / IntegrateMcp / EventBus source)
  └── Lab EventBus (not packed)   → outbox → RabbitMQ → inbox → handlers
        ▲
Aspire: Postgres │ Redis │ RabbitMQ │ Memcached │ SigNoz
```

### Production / reusable vs experiment-only

| Kind | Location | Packed? |
|------|----------|---------|
| BuildingBlocks (Mediator, Mcp, Idempotency, Pagination.EF, Telemetry, Aspire.Hosting.SigNoz) | `src/BuildingBlocks/` | Yes (per-package tags / workflows) |
| Pagination IR / Dapper | `src/BuildingBlocks/Pagination*` | IR bundled; Dapper lab-only |
| EventBus outbox/inbox/DLQ | `src/Lab/EventBus` | **No** |
| YARP + Memcached rate limit | `src/Lab/FeatureFusion.ApiGateway` | **No** |
| Feature-flag demos | FeatureFusion | **No** |
| Experiment helpers | `tests/Lab/IntegrationTests/Infrastructure/` | **No** (test observation) |
| Numbered experiments Exp 1–18 + MAF prototype | `tests/.../Experiments/` | **No** |

## Evidence families (Exp 1–18)

| Family | Experiments | Outcome |
|--------|-------------|---------|
| Keyset pagination abuse | 1–2 | Careless HTTP/MCP cursor clients characterized |
| HTTP Redis idempotency | 3, 4, 12, 15 | Miss/hit, concurrency, fingerprint, ProcessingTtl lease overlap → **BuildingBlocks.Idempotency 1.0.0** |
| MCP write / agent-client semantics | 6, 13, 14, 16 | Confirm+replay, regenerated keys amplify, concurrent same-key safe, **IMcpRateLimiter** bounds distinct-key storms |
| Outbox → bus → inbox happy path | 5, 8, 10 | HTTP + MCP parity; outbox row lifecycle |
| Consumer dedup / failure | 7, 9, 11, 17 | Inbox dedup; retry/DLQ; `EnableDeduplication` + `processed_messages` |
| Sync + async telemetry | TraceEvidence, 18 | In-process capture works; **W3C does not cross RabbitMQ** — correlate by `IntegrationEvent.Id` / `OrderId` |
| EventBus Lab seam | 19–20 | Observation baseline + publish-then-crash characterization (**branch end**) |
| MAF + Ollama + MCP | Prototype only | Research spike; not a numbered experiment |

## Extraction rule

Extract to BuildingBlocks only when **all** hold:

1. A falsifiable experiment (or package test matrix) proved the contract on real infra.
2. At least one host outside FeatureFusion would install it without the lab.
3. The public API can stay stable without pulling EventBus / YARP / FeatureManagement.
4. CI pack + release workflow exists (or is added in the same change).

Do **not** extract: Scenario DSL, one-off test gates, fixed-permit rate limiters used only in Exp 16, lease-hold handler wrappers, or EventBus until crash/propagation seams are deliberate product work.

## Package / versioning (current)

- Each packable project owns `<Version>` in its `.csproj` (e.g. `BuildingBlocks.Pagination.EntityFrameworkCore` `1.1.0`, `BuildingBlocks.Mcp` `1.0.0`).
- Release tags are package-prefixed (`mcp-v*`, `mediator-v*`, `telemetry-v*`, …) and must match `<Version>`.
- Per-package GitHub Actions: `*-yml` CI + `*-release.yml` pack / Trusted Publishing to nuget.org (`idempotency.yml` / `idempotency-release.yml` for this package).
- IntegrationTests / Exp 1–18 are **not** in CI (local Aspire/Docker).

## Roadmap (lab architecture — not experiment numbers)

| Phase | Intent | Status |
|-------|--------|--------|
| **1** | Lab charter + evidence-family map | **Done** |
| **2** | Collapse stable IntegrationTests observation helpers (≥3 identical) | **Done** |
| **3** | Lab EventBus observation seam + Exp 19 baseline + Exp 20 publish-then-crash | **Done** — MCP/EventBus research branch **closed** (see `docs/lab/MCP-EVENTBUS-RESEARCH-REPORT.md`) |

## MCP surface (lab today)

| Tool | Kind | Notes |
|------|------|-------|
| `orders.create` | Command | Confirmation + MCP memory idempotency |
| `products.list` | Query | Keyset pagination |
| `demo.echo` | Command | `Idempotent = false` smoke |
| `lab.ping` | Query | Minimal API → MCP |

## Related

- [Experiments catalog](../../tests/Lab/IntegrationTests/Experiments/README.md)
- [AGENTS.md](../../AGENTS.md)
- [BuildingBlocks getting started](../building-blocks/getting-started.md)
- [LinkedIn / Medium catalog](../linkedin-posts.md)
