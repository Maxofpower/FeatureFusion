# Pipeline behaviors

## Order

- Lower `order` = **outermost**.
- When `order` is omitted, registration index is used (first registered = outermost).
- `UseTelemetry()` is **not** a pipeline behavior — it wraps Send around the entire pipeline + handler when enabled.

```csharp
cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
cfg.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 100);
cfg.UseTelemetry(); // optional ActivitySource enrichment around Send
```

## Filters

```csharp
public sealed class MetricsOnCommands<TRequest, TResponse>
    : CommandPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    protected override async Task<TResponse> HandleCommand(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // metrics...
        return await next(ct);
    }
}
```

`QueryPipelineBehavior<,>` skips commands the same way.

## Cancellation

Tokens passed to `next(ct)` flow to the handler as-is (including `CancellationToken.None`).
