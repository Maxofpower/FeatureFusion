# LinkedIn posts catalog

Canonical map from [FeatureManagement](https://github.com/Maxofpower/FeatureManagement) features to LinkedIn deep-dives by [Mohammad Hasan Hosseini](https://www.linkedin.com/in/mhhoseini/).

Primary links are also mirrored on public types via XML `<remarks>` (e.g. `IMediator`, `IdempotentAttribute`) so IDEs show them without opening this file.

| Id | Title | Status | URL | Code map | Summary |
|----|-------|--------|-----|----------|---------|
| `mediator` | Manual Mediator + pipeline behaviors | published (related) | https://www.linkedin.com/feed/update/urn:li:activity:7311311587372367873/ | was `Infrastructure/CQRS`; now `src/BuildingBlocks.Mediator` | Custom CQRS Mediator with cached wrappers, void via `ICommand : ICommand<Unit>`, OTel-friendly behaviors. **Revision** → `mediator-building-blocks`. |
| `mediator-building-blocks` | Ready-to-use Mediator building blocks | planned | _add permalink when published_ | `src/BuildingBlocks.Mediator` + `docs/building-blocks/mediator.md` | LinkedIn **revision**: CQRS-first `ICommand`/`IQuery` Send + pipeline (void via `ICommand : ICommand<Unit>`). Publish/notifications intentionally out of v1. |
| `cursor-pagination` | Reusable generic bidirectional keyset (cursor) pagination | published | https://www.linkedin.com/feed/update/urn:li:activity:7325068550614708225/ | `src/FeatureFusion/Infrastructure/CursorPagination` | Type-safe Base64 cursors, dynamic sort, bidirectional nav, expression-tree filters for EF Core + CQRS. |
| `idempotency-cqrs` | Idempotency with CQRS commands | published | https://www.linkedin.com/feed/update/urn:li:activity:7303686809891356676/ | (pattern lab) | Command-level idempotency via reusable `IdentifiedCommand<T,R>` + ULID `Idempotency-Key`, intercepting before handlers. Complementary to Mediator building blocks — not core v1. |
| `idempotentfusion` | IdempotentFusion (REST API) | published | https://www.linkedin.com/feed/update/urn:li:activity:7309149985307029504/ | `src/FeatureFusion/Infrastructure/Filters/Idempotent*` | Redis-backed request status tracking (Processing/Completed/Failed), optional distributed lock, ULID keys. |
| `pubsub` | In-process EventPublisher | planned | _add permalink_ | sibling lab `pub-sub pattern` | In-process publish to consumers — **not** part of Mediator Send/pipeline v1. |

## How to maintain

1. When you publish a LinkedIn post tied to this repo, add a row here **in the same PR** that ships or documents the feature.
2. Link the same URL from the matching README section under **Related LinkedIn**.
3. Prefer activity permalinks (`urn:li:activity:…`) or `/posts/…` URLs — do not invent links.
