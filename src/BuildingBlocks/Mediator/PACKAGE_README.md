# BuildingBlocks.Mediator

CQRS-first **Send** + ordered **pipeline** for .NET 8+. Commands and queries, host-owned behaviors, optional traces and metrics, startup handler checks.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Mediator.svg)](https://www.nuget.org/packages/BuildingBlocks.Mediator)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**When to use:** you want manual pipeline order, typed command/query behaviors, optional OpenTelemetry around Send, and a built-in scanner (no Scrutor).

## What's new in 1.1.0

- Typed `ICommandPipelineBehavior` / `IQueryPipelineBehavior` — MS.DI does not construct the opposite kind
- `AddOpenCommandBehavior` / `AddOpenQueryBehavior` fail fast when the type is unconstrained
- Opt-in Send metrics on `UseTelemetry()` (histogram `mediator.send.duration`, counter `mediator.send`)
- Drop-in from 1.0.1 (`CommandPipelineBehavior` / `QueryPipelineBehavior` unchanged)

## Features

- **CQRS markers:** `ICommand` / `ICommand<T>` / `IQuery<T>` (no non-generic `IQuery`)
- **Void commands:** `ICommand : ICommand<Unit>` — pipeline binds to the real command type
- **Ordered pipeline:** open/closed behaviors with optional `order` (lower = outermost)
- **Typed command/query behaviors:** `ICommandPipelineBehavior` / `IQueryPipelineBehavior` — MS.DI does not construct them for the opposite kind
- **1.0 filter bases still work:** `CommandPipelineBehavior` / `QueryPipelineBehavior` skip the other kind at runtime
- **`UseTelemetry()`:** optional ActivitySource + Meter around Send (not a pipeline behavior)
- **`ValidateOnStartup`:** missing/duplicate handlers at registration
- **Exact-one handler** at Send, with clear errors
- **Open-generic handlers** closed on demand
- **Built-in scanner** — no Scrutor
- **Roslyn analyzers** BBM001 / BBM002 packed in the NuGet

## Install

```bash
dotnet add package BuildingBlocks.Mediator
```

Requires **.NET 8**, **.NET 9**, or **.NET 10**.

## Quick start

**1. Define a command and handler**

```csharp
public sealed record CreateOrder(string Product, int Qty) : ICommand<OrderId>;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, OrderId>
{
    public Task<OrderId> Handle(CreateOrder command, CancellationToken ct)
        => Task.FromResult(new OrderId(Guid.NewGuid()));
}
```

**2. Register**

```csharp
services.AddMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0); // host-owned
    cfg.UseTelemetry();          // traces + metrics (omit for zero overhead)
    cfg.ValidateOnStartup = true;
});
```

**3. Send** — prefer `ISender` at call sites

```csharp
await sender.Send(new CreateOrder("SKU-1", 2), ct);
var dto = await sender.Send(new GetOrder(id), ct);
await sender.Send(new CancelOrder(id), ct); // ICommand (void)
```

## Command-only vs query-only behaviors

Use constrained interfaces so a caching behavior is never constructed for a write, and an audit/transaction behavior is never constructed for a read:

```csharp
public sealed class AuditCommands<TCommand, TResponse> : ICommandPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(
        TCommand command, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => await next(ct);
}

public sealed class CacheQueries<TQuery, TResponse> : IQueryPipelineBehavior<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<TResponse> Handle(
        TQuery query, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => await next(ct);
}

cfg.AddOpenCommandBehavior(typeof(AuditCommands<,>), order: 10);
cfg.AddOpenQueryBehavior(typeof(CacheQueries<,>), order: 20);
// AddOpenBehavior(typeof(AuditCommands<,>)) also works — the constraint is on the type
```

`CommandPipelineBehavior` / `QueryPipelineBehavior` from 1.0.1 remain supported (runtime skip). Prefer the interfaces for new code.

## Telemetry

`UseTelemetry()` wraps **pipeline + handler** (not a behavior):

- **Traces:** ActivitySource `BuildingBlocks.Mediator`
- **Metrics:** Meter `BuildingBlocks.Mediator` — histogram `mediator.send.duration` (ms), counter `mediator.send` (`mediator.success`, `mediator.message_kind`, `mediator.request_name`)

```csharp
cfg.UseTelemetry(o =>
{
    o.ActivitySourceName = "BuildingBlocks.Mediator"; // MeterName copies this when unset
    o.EnableMetrics = true;   // default
    o.EnableLogging = true;
    o.RecordException = true;
});
```

Host OpenTelemetry (or `BuildingBlocks.Telemetry` with `IntegrateMediator = true`):

```csharp
.WithTracing(t => t.AddSource("BuildingBlocks.Mediator"))
.WithMetrics(m => m.AddMeter("BuildingBlocks.Mediator"));
```

Omit `UseTelemetry()` for zero library telemetry overhead. Set `EnableMetrics = false` to keep traces without meters.

Validation stays host-owned (FluentValidation + `AddOpenBehavior`). See the cookbook.

## What it is not (v1)

- Not a MediatR or messaging replacement
- No `Publish` / `INotification` (use your event bus)
- No streaming (`CreateStream`)
- No exception *handlers* that replace results (faults rethrow)
- No built-in FluentValidation
- Not fully Native AOT (runtime `MakeGenericType` wrappers)
- Open-generic handlers always resolve as Transient (ignore `HandlerLifetime`)

## Docs

- [Getting started](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/getting-started.md)
- [Pipeline behaviors](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/pipeline-behaviors.md)
- [Cookbook](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/cookbook.md)
- [Test matrix](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/TEST_MATRIX.md)
- [CHANGELOG](https://github.com/Maxofpower/FeatureFusion/blob/main/CHANGELOG.md)

Demo host: **FeatureFusion** in the same repository.

## License

MIT — Copyright (c) 2026 Mohammad Hasan Hosseini
