# BuildingBlocks.Mediator — public interface freeze (v1.1)

**Status:** NuGet 1.1.0 (CQRS-first Send + pipeline; typed command/query behaviors; UseTelemetry traces + metrics)  
**Date:** 2026-08-27  
**Author:** Mohammad Hasan Hosseini  
**LinkedIn (prior):** https://www.linkedin.com/feed/update/urn:li:activity:7311311587372367873/  
**Catalog:** `docs/linkedin-posts.md` → `mediator` / `mediator-building-blocks`

Docs: [getting-started](getting-started.md) · [design](mediator-design.md) · [TEST_MATRIX](TEST_MATRIX.md)

---

## 5-minute adopt

```csharp
services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(MyHandler).Assembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
    cfg.AddOpenCommandBehavior(typeof(AuditCommands<,>), order: 10);
    cfg.UseTelemetry(o =>
    {
        o.ActivitySourceName = "BuildingBlocks.Mediator";
        o.EnableMetrics = true;
        o.RecordException = true;
        o.EnableLogging = true;
    });
    cfg.ValidateOnStartup = true;
});

public sealed record CreateOrder : ICommand<OrderId>;
public sealed record GetOrder(OrderId Id) : IQuery<OrderDto>;
// Illegal: class Bad : IQuery  — no non-generic IQuery (compile error)
```

Prefer `ISender` at call sites.

### Pipeline registration order

- Explicit `order` (lower = outermost) beats registration order when set.
- When omitted, registration index is used (first = outermost).
- `UseTelemetry()` optionally wraps Send with an ActivitySource and Meter (pipeline + handler); omit for no telemetry.

### Host validation (optional)

1. Register FluentValidation as closed `IValidator<T>`.
2. `cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0)` then `UseTelemetry()`.
3. Behavior: no validators → skip; else aggregate → throw `FluentValidation.ValidationException`.
4. HTTP: `IExceptionHandler` → `ValidationProblemDetails` (400).

See [cookbook.md](cookbook.md).

---

## Compile-time CQRS rules

| Message | No payload | With response |
|---------|------------|---------------|
| Command | `ICommand` (ok) | `ICommand<TResponse>` |
| Query | **Invalid** — no `IQuery` | `IQuery<TResponse>` only |

---

## Public contracts

`ISender`: void `Send(ICommand)`, `Send(ICommand<T>)`, `Send(IQuery<T>)`, `Send(object)`.  
`IMediator : ISender` (Send only — no Publish).  
Handlers: `ICommandHandler<>` / `ICommandHandler<,>` / `IQueryHandler<,>`.  
Pipeline: `IPipelineBehavior<,>` — order via registration or explicit `order`.  
Typed filters: `ICommandPipelineBehavior` / `IQueryPipelineBehavior` (preferred); `AddOpenCommandBehavior` / `AddOpenQueryBehavior`.  
1.0 filters: `CommandPipelineBehavior` / `QueryPipelineBehavior` (runtime skip; not obsolete).  
Void commands: `ICommand : ICommand<Unit>` — behaviors on concrete type + `Unit`.  
Scanner: built-in (no Scrutor).

### Registration enrichment

| API | Includes | Excludes |
|-----|----------|----------|
| `UseTelemetry` | Activity/traces, optional Meter (`mediator.send` / `mediator.send.duration`), optional ILogger, exception **observation** (rethrow) | Exception *handlers* |
| `AddOpenBehavior` | Host cross-cutting (validation, extra metrics, …) | — |
| `AddOpenCommandBehavior` / `AddOpenQueryBehavior` | Constrained open generics; fail fast if the type is not command/query-only | Unconstrained `IPipelineBehavior<,>` |
| `HandlerLifetime` | Lifetime for discovered handlers (default Transient) | Open-generic always Transient |
| `ValidateOnStartup` | Exactly one handler per public/nested-public closed message | — |

1.0.1 hosts upgrade without code changes. New interfaces are optional.

---

## Layout

`src/BuildingBlocks/Mediator/` — Abstractions, Implementation, DependencyInjection, Pipeline, Telemetry, analyzers packed from `BuildingBlocks.Mediator.Analyzers`.  
Demo host validation: `src/Lab/FeatureFusion/Infrastructure/Behaviors/ValidationBehavior.cs`.
