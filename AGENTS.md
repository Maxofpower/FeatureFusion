# FeatureFusion — agent notes

Public **.NET lab** plus extracted MIT **BuildingBlocks**. The application is the laboratory. A package is a reusable result that earned a boundary.

Do not treat FeatureFusion as a framework. Install a BuildingBlock only when it solves a problem the host actually has.

## Lab vs package

| Kind | What it is | Agent docs |
|------|------------|------------|
| **Lab** | Runnable API + Aspire AppHost (`src/Lab/`) | This file + [docs/lab](docs/lab/README.md) + root README |
| **Packable BuildingBlock** | NuGet on nuget.org | `src/BuildingBlocks/<Name>/AGENTS.md` + `PACKAGE_README.md` |
| **In-repo sibling** | Pagination IR / Dapper — not packed | `Pagination/AGENTS.md`, `Pagination.Dapper/AGENTS.md` |
| **Lab-only** | EventBus, YARP rate limit, feature flags | Code + [linkedin catalog](docs/linkedin-posts.md) — no package `AGENTS.md` |

**Lab charter (evidence families, extraction rules, roadmap):** [docs/lab/README.md](docs/lab/README.md). Behavioral experiment catalog: [Experiments README](tests/Lab/IntegrationTests/Experiments/README.md) (Exp 1–18 + MAF prototype).

## Packages (read these first)

| Package | When to choose | AGENTS.md |
|---------|----------------|-----------|
| `BuildingBlocks.Mediator` | CQRS Send + ordered pipeline | [Mediator](src/BuildingBlocks/Mediator/AGENTS.md) |
| `BuildingBlocks.Pagination.EntityFrameworkCore` | Keyset (cursor) paging for EF Core | [Pagination.EF](src/BuildingBlocks/Pagination.EntityFrameworkCore/AGENTS.md) |
| `BuildingBlocks.Mcp` | Same logic as HTTP as deny-by-default MCP tools | [Mcp](src/BuildingBlocks/Mcp/AGENTS.md) |
| `BuildingBlocks.Idempotency` | HTTP Idempotency-Key + optional Redis lock | [Idempotency](src/BuildingBlocks/Idempotency/AGENTS.md) |
| `BuildingBlocks.Telemetry` | Config-driven OpenTelemetry (not a SigNoz SDK) | [Telemetry](src/BuildingBlocks/Telemetry/AGENTS.md) |
| `BuildingBlocks.Aspire.Hosting.SigNoz` | Local AppHost SigNoz only (net10) | [SigNoz](src/BuildingBlocks/Aspire.Hosting.SigNoz/AGENTS.md) |

Pagination IR (`BuildingBlocks.Pagination`) is bundled into the EF Core nupkg. Do not pack it. Dapper pagination is a lab project.

## Human docs

- [README](README.md) — lab + package overview
- [docs/lab](docs/lab/README.md) — lab charter, evidence families, extraction rules
- [docs/building-blocks](docs/building-blocks/) — getting started, mediator, pagination, MCP, telemetry
- [docs/linkedin-posts.md](docs/linkedin-posts.md) — post ↔ code map
- [tests/Lab/IntegrationTests/Experiments/README.md](tests/Lab/IntegrationTests/Experiments/README.md) — behavioral integration experiments (Lab convention + catalog Exp 1–18)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [llms.txt](llms.txt) — URL index for crawlers

## Lab-only (not NuGet)

- RabbitMQ EventBus + outbox/inbox — `src/Lab/EventBus`
- YARP + Memcached rate limiting — `src/Lab/FeatureFusion.ApiGateway`
- Feature-management filters — lab FeatureFusion

## Related

Writing map: [docs/linkedin-posts.md](docs/linkedin-posts.md). Do not put LinkedIn URLs in NuGet XML docs or `PACKAGE_README.md`.
