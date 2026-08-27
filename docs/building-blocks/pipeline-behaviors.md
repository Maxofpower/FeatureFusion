# Pipeline behaviors

## Order

- Lower `order` = **outermost**.
- When `order` is omitted, registration index is used (first registered = outermost).
- `UseTelemetry()` is **not** a pipeline behavior — it wraps Send around the entire pipeline + handler when enabled (Activity + optional Meter).

```csharp
cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
cfg.AddOpenCommandBehavior(typeof(AuditCommands<,>), order: 10);
cfg.AddOpenQueryBehavior(typeof(CacheQueries<,>), order: 20);
cfg.UseTelemetry();
```

## Typed command / query behaviors (preferred)

`ICommandPipelineBehavior<TCommand, TResponse> where TCommand : ICommand<TResponse>` and
`IQueryPipelineBehavior<TQuery, TResponse> where TQuery : IQuery<TResponse>` are closed by MS.DI
**only** for matching message kinds. An audit behavior is never constructed for a query.

```csharp
public sealed class AuditCommands<TCommand, TResponse> : ICommandPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(
        TCommand command, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => await next(ct);
}

cfg.AddOpenCommandBehavior(typeof(AuditCommands<,>), order: 10);
// AddOpenBehavior(typeof(AuditCommands<,>)) also works — the constraint is on the type
```

`AddOpenCommandBehavior` / `AddOpenQueryBehavior` fail fast if the type does not implement the matching interface.

## 1.0 runtime-skip filters (still supported)

Unconstrained `CommandPipelineBehavior` / `QueryPipelineBehavior` are constructed for every Send and skip the opposite kind at runtime. Drop-in from 1.0.1 — not obsolete.

```csharp
public sealed class MetricsOnCommands<TRequest, TResponse>
    : CommandPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    protected override async Task<TResponse> HandleCommand(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => await next(ct);
}
```

## Cancellation

Tokens passed to `next(ct)` flow to the handler as-is (including `CancellationToken.None`).
