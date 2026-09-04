# BuildingBlocks.Mediator

CQRS-first **Send** + ordered **pipeline** for .NET 8+. Commands and queries, host-owned behaviors, optional traces and metrics, startup handler checks.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Mediator.svg)](https://www.nuget.org/packages/BuildingBlocks.Mediator)
[![GitHub Release](https://img.shields.io/github/v/release/Maxofpower/FeatureFusion?filter=mediator-v*&logo=github&label=GitHub%20Release)](https://github.com/Maxofpower/FeatureFusion/releases?q=mediator-v)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**When to use:** you want manual pipeline order, typed command/query behaviors, optional OpenTelemetry around Send, and a built-in scanner (no Scrutor).

## What's new in 1.1.0

- Typed `ICommandPipelineBehavior` / `IQueryPipelineBehavior` — MS.DI does not construct the opposite kind
- `AddOpenCommandBehavior` / `AddOpenQueryBehavior` fail fast when the type is unconstrained
- Opt-in Send metrics on `UseTelemetry()` (histogram `mediator.send.duration`, counter `mediator.send`)
- Drop-in from 1.0.1 (`CommandPipelineBehavior` / `QueryPipelineBehavior` unchanged)

## Install

```bash
dotnet add package BuildingBlocks.Mediator
```

Requires **.NET 8**, **.NET 9**, or **.NET 10**.

## Quick start

```csharp
public sealed record CreateOrder(string Product, int Qty) : ICommand<Guid>;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, Guid>
{
    public Task<Guid> Handle(CreateOrder command, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());
}

services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
    cfg.UseTelemetry();
    cfg.ValidateOnStartup = true;
});

await sender.Send(new CreateOrder("SKU-1", 2), ct);
```

Prefer `ISender` at call sites. Host OpenTelemetry: `AddSource` / `AddMeter` `"BuildingBlocks.Mediator"` or Telemetry `IntegrateMediator`.

## Quick start — all options

Markers: `ICommand` / `ICommand<T>` / `IQuery<T>` (no public `IRequest`, no non-generic `IQuery`). Void writes: `ICommand : ICommand<Unit>`. Prefer `ISender` at call sites (`IMediator` is the same Send surface).

```csharp
public sealed record CreateOrder(string Product, int Qty) : ICommand<Guid>;
public sealed record CancelOrder(Guid Id) : ICommand;
public sealed record GetOrder(Guid Id) : IQuery<OrderDto>;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, Guid>
{
    public Task<Guid> Handle(CreateOrder command, CancellationToken ct)
        => Task.FromResult(Guid.NewGuid());
}

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => next(ct);
}

public sealed class AuditCommands<TCommand, TResponse> : ICommandPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public Task<TResponse> Handle(TCommand command, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => next(ct);
}

public sealed class CacheQueries<TQuery, TResponse> : IQueryPipelineBehavior<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public Task<TResponse> Handle(TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => next(ct);
}

// 1.0.1 bases still work (runtime skip of the other kind): CommandPipelineBehavior / QueryPipelineBehavior

services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(CreateOrderHandler).Assembly);
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>(); // same assembly is deduped

    cfg.Lifetime = ServiceLifetime.Scoped;           // ISender / IMediator — default Scoped
    cfg.HandlerLifetime = ServiceLifetime.Transient; // discovered handlers — default Transient
    // Open-generic handlers always resolve Transient (ignore HandlerLifetime)

    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0); // lower = outermost; omit order → registration order
    cfg.AddOpenCommandBehavior(typeof(AuditCommands<,>), order: 10);
    cfg.AddOpenQueryBehavior(typeof(CacheQueries<,>), order: 20);
    // cfg.AddOpenBehavior(typeof(AuditCommands<,>)); // also OK — constraint is on the type
    // cfg.AddBehavior<ClosedLoggingBehavior>(order: 5); // closed (non-open) behavior

    cfg.UseTelemetry(o =>
    {
        o.ActivitySourceName = "BuildingBlocks.Mediator"; // default; host must AddSource this name
        o.MeterName = "";               // empty → copies ActivitySourceName
        o.EnableMetrics = true;         // histogram mediator.send.duration (ms), counter mediator.send
        o.EnableLogging = true;         // ILogger start/end/errors
        o.RecordException = true;       // Activity error status
    });
    // Omit UseTelemetry() for zero library telemetry overhead. EnableMetrics = false keeps traces without meters.

    cfg.ValidateOnStartup = true; // exact-one handler per message at registration
});

await sender.Send(new CreateOrder("SKU-1", 2), ct);           // ICommand<T>
await sender.Send(new GetOrder(id), ct);                      // IQuery<T>
await sender.Send(new CancelOrder(id), ct);                   // ICommand (void)
await sender.Send((object)new CreateOrder("SKU-1", 2), ct);   // runtime type (MCP / dynamic)
```

Host OpenTelemetry (or `BuildingBlocks.Telemetry` with `IntegrateMediator = true`):

```csharp
.WithTracing(t => t.AddSource("BuildingBlocks.Mediator"))
.WithMetrics(m => m.AddMeter("BuildingBlocks.Mediator"));
```

Scanner is built-in (no Scrutor). Analyzers **BBM001 / BBM002** ship in the NuGet. Validation stays host-owned (FluentValidation + `AddOpenBehavior`). Open-generic handlers (`Handler<T> : ICommandHandler<Cmd<T>, T>`) close on demand at Send.

## What it is not (v1)

- Not a MediatR or messaging replacement
- No `Publish` / `INotification` (use your event bus)
- No streaming (`CreateStream`)
- No exception *handlers* that replace results (faults rethrow)
- No built-in FluentValidation
- Not fully Native AOT (runtime `MakeGenericType` wrappers)

## Docs

- [Getting started](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/getting-started.md)
- [Pipeline behaviors](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/pipeline-behaviors.md)
- [Cookbook](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/cookbook.md)
- [Test matrix](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/TEST_MATRIX.md)
- [CHANGELOG](https://github.com/Maxofpower/FeatureFusion/blob/main/CHANGELOG.md)

Demo host: **FeatureFusion** in the same repository.

## License

MIT — Copyright (c) 2026 Mohammad Hasan Hosseini
