# Changelog

All notable changes to **BuildingBlocks.Mediator** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-08-08

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