# BuildingBlocks.Mediator — agent notes

CQRS **Send** + ordered pipeline for .NET 8/9/10. Install: `dotnet add package BuildingBlocks.Mediator`.

## When to choose this

Manual pipeline order, typed command/query behaviors, optional OpenTelemetry around Send, built-in scanner (no Scrutor). Prefer `ISender` at call sites.

## Register and send

```csharp
services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();
    cfg.AddOpenCommandBehavior(typeof(AuditCommands<,>), order: 10);
    cfg.AddOpenQueryBehavior(typeof(CacheQueries<,>), order: 20);
    cfg.UseTelemetry();
    cfg.ValidateOnStartup = true;
});

await sender.Send(new CreateOrder("SKU-1", 2), ct);
```

Markers: `ICommand` / `ICommand<T>` / `IQuery<T>` (no non-generic `IQuery`). Void writes: `ICommand : ICommand<Unit>`.

Typed behaviors: `ICommandPipelineBehavior<,>` / `IQueryPipelineBehavior<,>` so MS.DI does not construct the opposite kind. 1.0 bases `CommandPipelineBehavior` / `QueryPipelineBehavior` still work (runtime skip).

## Do not use for

Publish / `INotification`, streaming, MediatR-style notification handlers, built-in FluentValidation, Native AOT.

Host OTel: `AddSource("BuildingBlocks.Mediator")` and `AddMeter("BuildingBlocks.Mediator")` (or `BuildingBlocks.Telemetry` with `IntegrateMediator = true`).
