# BuildingBlocks.Mediator — agent notes

CQRS **Send** + ordered pipeline for .NET 8/9/10. Install: `dotnet add package BuildingBlocks.Mediator`. Prefer `ISender`. Markers: `ICommand` / `ICommand<T>` / `IQuery<T>`. Void: `ICommand : ICommand<Unit>`.

```csharp
services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();
    cfg.Lifetime = ServiceLifetime.Scoped;
    cfg.HandlerLifetime = ServiceLifetime.Transient;
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
    cfg.AddOpenCommandBehavior(typeof(AuditCommands<,>), order: 10);
    cfg.AddOpenQueryBehavior(typeof(CacheQueries<,>), order: 20);
    cfg.UseTelemetry(o =>
    {
        o.ActivitySourceName = "BuildingBlocks.Mediator";
        o.EnableMetrics = true;
        o.EnableLogging = true;
        o.RecordException = true;
    });
    cfg.ValidateOnStartup = true;
});
```

Typed behaviors: `ICommandPipelineBehavior` / `IQueryPipelineBehavior`. Closed: `AddBehavior<T>()`. Host OTel: `AddSource` + `AddMeter` or Telemetry `IntegrateMediator`. No Publish / stream / built-in FluentValidation.
