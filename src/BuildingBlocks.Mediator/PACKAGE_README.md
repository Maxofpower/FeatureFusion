# BuildingBlocks.Mediator

CQRS-first **Send** + ordered **pipeline** mediator for .NET — `ICommand` / `IQuery`, `ISender`, host behaviors, optional telemetry, and startup handler validation.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Mediator.svg)](https://www.nuget.org/packages/BuildingBlocks.Mediator)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> **Disclaimer:** BuildingBlocks.Mediator is **not** designed to replace other mediator or messaging packages. It is a focused CQRS Send + pipeline building block for developers who want **manual control** over design patterns — registration, pipeline order, validation, telemetry, and host composition — rather than a batteries-included framework.

## Requirements

- **.NET 10** (`net10.0`) — v1 ships this TFM only (no multi-targeting)

## Install

```bash
dotnet add package BuildingBlocks.Mediator
```

## 60-second quick start

```csharp
services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
    cfg.UseTelemetry();
    cfg.ValidateOnStartup = true;
});

public sealed record CreateOrder(string Product, int Qty) : ICommand<OrderId>;
public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, OrderId>
{
    public Task<OrderId> Handle(CreateOrder command, CancellationToken ct)
        => Task.FromResult(new OrderId(Guid.NewGuid()));
}

// Prefer ISender at call sites
await sender.Send(new CreateOrder("SKU-1", 2), ct);
```

## Features

- CQRS markers: `ICommand` / `ICommand<T>` / `IQuery<T>` (no non-generic `IQuery`)
- Void commands via `ICommand : ICommand<Unit>` (pipeline on the real type)
- Ordered open/closed pipeline behaviors (`AddOpenBehavior` + optional `order`)
- `CommandPipelineBehavior` / `QueryPipelineBehavior` filter bases
- `UseTelemetry` — optional ActivitySource enrichment around Send (wraps pipeline + handler; not a behavior)
- `ValidateOnStartup` — missing/duplicate handler checks at registration
- `HandlerLifetime` — configure discovered handler lifetime (default Transient)
- Exact-one handler resolution at Send (clear errors for missing/ambiguous handlers)
- Open-generic handlers — scanned and closed on demand
- Built-in assembly scanner — **no Scrutor dependency**

## What it is not (v1)

- No `Publish` / `INotification`
- No streaming (`CreateStream`)
- No exception *handlers* that replace results (faults rethrow)
- No built-in FluentValidation or metrics (host `AddOpenBehavior`)
- Not fully Native AOT (runtime `MakeGenericType` wrappers)
- Open-generic handlers always resolve as Transient (ignore `HandlerLifetime`)

## Docs

- [Getting started](https://github.com/Maxofpower/FeatureManagement/blob/main/docs/building-blocks/getting-started.md)
- [Pipeline behaviors](https://github.com/Maxofpower/FeatureManagement/blob/main/docs/building-blocks/pipeline-behaviors.md)
- [Cookbook](https://github.com/Maxofpower/FeatureManagement/blob/main/docs/building-blocks/cookbook.md)
- [v1.0 test matrix](https://github.com/Maxofpower/FeatureManagement/blob/main/docs/building-blocks/TEST_MATRIX.md)
- [CHANGELOG](https://github.com/Maxofpower/FeatureManagement/blob/main/CHANGELOG.md)

Demo host: **FeatureFusion** in the same repository.

## License

MIT — Copyright (c) 2026 Mohammad Hasan Hosseini
