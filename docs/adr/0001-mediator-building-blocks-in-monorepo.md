# ADR 0001 — BuildingBlocks.Mediator in the FeatureManagement monorepo

- **Status:** Accepted (CQRS-first + UseTelemetry amendment)
- **Date:** 2026-08-07
- **Deciders:** Mohammad Hasan Hosseini
- **Related:** [docs/building-blocks/mediator.md](../building-blocks/mediator.md)

## Decision

1. Monorepo library `BuildingBlocks.Mediator`; FeatureFusion via `ProjectReference`.
2. Public CQRS: `ICommand` / `ICommand<T>` / `IQuery<T>` — no public `IRequest`; no void `IQuery`.
3. Send + ordered pipeline; void via `ICommand : ICommand<Unit>`; no Publish.
4. `UseTelemetry` = registration enrichment for Activity + logging + exception observation (rethrow). **Metrics** and **validation** via host `AddOpenBehavior` (FluentValidation `ValidationBehavior` pattern — not library-owned).
5. Non-goals: streams, exception *handlers* (recover/replace), pre/post processors, open generic handler closing, Publish, built-in metrics/validation packages.
6. Demo app: `cfg.AddOpenBehavior(typeof(ValidationBehavior<,>))` then `cfg.UseTelemetry()`; custom cross-cutting remains host-owned.

## Consequences

Clear CQRS teaching surface; telemetry opt-in without forcing a host behavior type; metrics policy stays with the app.
