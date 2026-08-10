# ADR 0000: .NET 10 + Aspire 13 upgrade baseline

## Status

Accepted (Phase 0)

## Context

The public FeatureManagement lab targeted `net9.0` with Aspire 9.x and RabbitMQ.Client 6 (`IModel`). Moving to .NET 10 requires a coherent Aspire line. Aspire 13 pins RabbitMQ.Client 7 (`IChannel`, fully async APIs).

## Decision

- Target **`net10.0`** for all projects.
- Use **Aspire 13.4.x** (not Aspire 9.x as a workaround).
- Migrate EventBus to **RabbitMQ.Client 7**.
- Keep Swashbuckle on OpenAPI 1.x-compatible packages for Phase 0 (avoid OpenAPI 2 rewrite of schema filters).
- Document LinkedIn posts in `docs/linkedin-posts.md`.

## Consequences

- EventBus and its tests must use async channel APIs (`CreateChannelAsync`, `BasicPublishAsync`, etc.).
- Dockerfile base images are `sdk:10.0` / `aspnet:10.0`.
- RabbitMQ.Client 7 migration of domain retry-header mutation on received messages is limited (read-only properties); requeue/DLQ behavior relies on broker semantics.
