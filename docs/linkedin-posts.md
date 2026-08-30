# LinkedIn posts catalog

Canonical map from [FeatureFusion](https://github.com/Maxofpower/FeatureFusion) (formerly FeatureManagement) features to LinkedIn deep-dives by [Mohammad Hasan Hosseini](https://www.linkedin.com/in/mhhoseini/).

Primary links are also mirrored on public types via XML `<remarks>` (e.g. `IMediator`, `IdempotentAttribute`) so IDEs show them without opening this file.

| Id | Title | Status | URL | Code map | Summary |
|----|-------|--------|-----|----------|---------|
| `mediator` | Manual Mediator + pipeline behaviors | published | https://www.linkedin.com/feed/update/urn:li:activity:7311311587372367873/ | was `Infrastructure/CQRS`; now `src/BuildingBlocks/Mediator` | Custom CQRS Mediator with cached wrappers, void via `ICommand : ICommand<Unit>`, OTel-friendly behaviors. Follow-up NuGet post → `mediator-building-blocks`. |
| `mediator-building-blocks` | BuildingBlocks.Mediator NuGet (v1.0.1) | published | https://lnkd.in/p/eU5TsuR4 | `src/BuildingBlocks/Mediator` + `docs/building-blocks/mediator.md` | Packaged CQRS Send + ordered pipeline (void via `ICommand : ICommand<Unit>`). Publish/notifications out of v1. Prior deep-dive → `mediator`. |
| `cursor-pagination` | Reusable generic bidirectional keyset (cursor) pagination | published | https://www.linkedin.com/feed/update/urn:li:activity:7325068550614708225/ | `src/BuildingBlocks/Pagination.EntityFrameworkCore` + lab `ProductService` | Typed `SortKey`, opaque cursors, one EF Core nupkg. Optional `QueryHint` (default none). Lab: MVC `POST /api/v2/Product/products`, Minimal API `GET /api/v2/products-page` (POST kept), Dapper `products-dapper`, MCP `products.list` — one `GetProductsQuery`. |
| `idempotency-cqrs` | Idempotency with CQRS commands | published | https://www.linkedin.com/feed/update/urn:li:activity:7303686809891356676/ | (pattern lab) | Command-level idempotency via reusable `IdentifiedCommand<T,R>` + ULID `Idempotency-Key`, intercepting before handlers. Complementary to Mediator building blocks — not core v1. |
| `idempotentfusion` | IdempotentFusion (REST API) | published | https://www.linkedin.com/feed/update/urn:li:activity:7309149985307029504/ | `src/Lab/FeatureFusion/Infrastructure/Filters/Idempotent*` | Redis-backed request status tracking (Processing/Completed/Failed), optional distributed lock, ULID keys. |
| `mcp-message-tools` | Message types as MCP tools | planned | _add permalink when published_ | `src/BuildingBlocks/Mcp` + `docs/building-blocks/mcp.md` | Map `[McpTool]` commands/queries **or** public static endpoint methods **or** `MapTool` handlers to the official MCP SDK. Same logic as HTTP. Deny-by-default catalog. Not OpenAPI, not a SOLID linter. |
| `pubsub` | In-process EventPublisher | planned | _add permalink_ | sibling lab `pub-sub pattern` | In-process publish to consumers — **not** part of Mediator Send/pipeline v1. |

## How to maintain

1. When you publish a LinkedIn post tied to this repo, add a row here **in the same PR** that ships or documents the feature.
2. Link the same URL from the matching README section under **Related LinkedIn**.
3. Prefer activity permalinks (`urn:li:activity:…`) or `/posts/…` URLs — do not invent links.
