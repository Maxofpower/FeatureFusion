# BuildingBlocks.Mediator — public interface freeze (v1)

**Status:** NuGet-ready 1.0.0 (CQRS-first Send + pipeline; no Scrutor)  
**Date:** 2026-08-08  
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
    // Optional ActivitySource enrichment around Send (not a pipeline behavior)
    cfg.UseTelemetry(o =>
    {
        o.ActivitySourceName = "BuildingBlocks.Mediator";
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
- `UseTelemetry()` optionally wraps Send with an ActivitySource (pipeline + handler); omit for no telemetry.

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
Filters: `CommandPipelineBehavior` / `QueryPipelineBehavior`.  
Void commands: `ICommand : ICommand<Unit>` — behaviors on concrete type + `Unit`.  
Scanner: built-in (no Scrutor).

### Registration enrichment

| API | Includes | Excludes |
|-----|----------|----------|
| `UseTelemetry` | Activity/traces, optional ILogger, exception **observation** (rethrow) | Metrics, exception *handlers* |
| `AddOpenBehavior` | Host cross-cutting (metrics, validation, …) | — |
| `HandlerLifetime` | Lifetime for discovered handlers (default Transient) | Open-generic always Transient |
| `ValidateOnStartup` | Exactly one handler per public/nested-public closed message | — |

---

## Layout

`src/BuildingBlocks.Mediator/` — Abstractions, Implementation, DependencyInjection, Pipeline, Telemetry, analyzers packed from `BuildingBlocks.Mediator.Analyzers`.  
Demo host validation: `FeatureFusion/Infrastructure/Behaviors/ValidationBehavior.cs`.
