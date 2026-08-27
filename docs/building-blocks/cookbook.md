# Cookbook — host pipeline recipes

BuildingBlocks.Mediator does **not** ship FluentValidation. Register host validation with `AddOpenBehavior`. Send metrics are opt-in via `UseTelemetry()` (Meter); extra host metrics can still be a behavior.

## Validation (FluentValidation)

```csharp
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        => _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct = default)
    {
        if (!_validators.Any())
            return await next(ct);

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, ct))))
            .SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next(ct);
    }
}

// DI: register closed IValidator<T> + cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
```

Map `ValidationException` to HTTP 400 via `IExceptionHandler` (see FeatureFusion demo).

## Timeout

```csharp
public sealed class TimeoutBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        return await next(cts.Token);
    }
}

// cfg.AddOpenBehavior(typeof(TimeoutBehavior<,>), order: 10);
```

## Retry

```csharp
public sealed class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int MaxAttempts = 3;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct = default)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await next(ct);
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        throw last!;
    }
}

// cfg.AddOpenBehavior(typeof(RetryBehavior<,>), order: 20);
```

Place timeout outside retry (lower `order`) so each attempt shares the overall deadline, or invert if each attempt should get a fresh timeout.

## Logging

Open `IPipelineBehavior<,>` that logs request type before/after `next`. Place outside telemetry (`order` lower than `int.MaxValue`).

## Metrics

`UseTelemetry()` records histogram `mediator.send.duration` (ms) and counter `mediator.send` on meter `BuildingBlocks.Mediator` unless `EnableMetrics = false`. Extra RED/business metrics stay host-owned via `AddOpenBehavior` or `ICommandPipelineBehavior`.

## Command-only / query-only

Prefer `ICommandPipelineBehavior` / `IQueryPipelineBehavior` (and `AddOpenCommandBehavior` / `AddOpenQueryBehavior`) so the opposite kind is never constructed. `CommandPipelineBehavior` / `QueryPipelineBehavior` remain valid 1.0.1 runtime-skip bases. See [pipeline-behaviors.md](pipeline-behaviors.md).

## Caching

Implement as an open behavior around queries. Short-circuit by not calling `next` when serving from cache.

## Open-generic handlers

Handlers like `Handler<T> : ICommandHandler<EchoCommand<T>, T>` cannot be registered as native MS DI open generics (arity mismatch). The scanner records them and closes them on demand when you `Send`.

```csharp
public sealed record EchoCommand<T>(T Value) : ICommand<T>;

public sealed class OpenEchoHandler<T> : ICommandHandler<EchoCommand<T>, T>
{
    public Task<T> Handle(EchoCommand<T> command, CancellationToken ct)
        => Task.FromResult(command.Value);
}

// Discovered automatically via RegisterServicesFromAssembly...
await sender.Send(new EchoCommand<string>("hi"), ct); // "hi"
```

**v1 note:** open-generic handlers always resolve as Transient via `ActivatorUtilities`, regardless of `HandlerLifetime`. An explicit closed registration for the same closed interface wins over an open-generic match. Multiple open matches throw at Send time.
