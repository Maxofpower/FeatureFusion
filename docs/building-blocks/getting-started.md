# Getting started — BuildingBlocks.Mediator

> **Disclaimer:** BuildingBlocks.Mediator is **not** designed to replace other mediator or messaging packages. It is a focused CQRS Send + pipeline building block for developers who want **manual control** over design patterns — registration, pipeline order, validation, telemetry, and host composition — rather than a batteries-included framework.

**Requires .NET 10** (`net10.0`).

## Install

```bash
dotnet add package BuildingBlocks.Mediator
```

Or project reference in this monorepo:

```xml
<ProjectReference Include="..\BuildingBlocks.Mediator\BuildingBlocks.Mediator.csproj" />
```

## Register

```csharp
services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();
    // cfg.HandlerLifetime = ServiceLifetime.Transient; // default — safest
    // cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
    cfg.UseTelemetry();
    cfg.ValidateOnStartup = true; // optional
});
```

Prefer **`ISender`** at call sites (narrower than `IMediator`).

### Handler lifetime

| Setting | Meaning |
|---------|---------|
| `HandlerLifetime = Transient` (default) | New handler instance per `Send` |
| `Scoped` | One instance per DI scope (typically per HTTP request) |
| `Singleton` | One instance for the process — must be thread-safe and must **not** depend on scoped services (captive dependency) |

`Lifetime` (for `ISender` / `IMediator`) stays **Scoped** by default and is independent of `HandlerLifetime`. Open-generic handlers always resolve as Transient (ActivatorUtilities) regardless of `HandlerLifetime`.

### Cancellation

`CancellationToken` is cooperative and flows through Send → optional telemetry → behaviors → handler. Always call `await next(ct)` in behaviors — `await next()` drops the token to `CancellationToken.None` for everything downstream. The library does not impose timeouts; use a timeout behavior (see [cookbook.md](cookbook.md)) when you need one.

## Define messages and handlers

```csharp
public sealed record CreateOrder(string Product, int Qty) : ICommand<Guid>;
public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, Guid>
{
    public Task<Guid> Handle(CreateOrder command, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());
}

public sealed record GetOrder(Guid Id) : IQuery<OrderDto>;
public sealed class GetOrderHandler : IQueryHandler<GetOrder, OrderDto> { /* ... */ }

public sealed record CancelOrder(Guid Id) : ICommand;
public sealed class CancelOrderHandler : ICommandHandler<CancelOrder>
{
    public Task Handle(CancelOrder command, CancellationToken ct) => Task.CompletedTask;
}
```

## Send

```csharp
await sender.Send(new CreateOrder("SKU", 1), ct);
var dto = await sender.Send(new GetOrder(id), ct);
await sender.Send(new CancelOrder(id), ct);
```

Next: [concepts.md](concepts.md) · [pipeline-behaviors.md](pipeline-behaviors.md) · [cookbook.md](cookbook.md)
