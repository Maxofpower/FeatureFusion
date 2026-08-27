using System.Collections.Concurrent;
using System.Diagnostics;

namespace BuildingBlocks.Mediator.Tests;

public sealed record CreateOrder(string Product, int Quantity) : ICommand<OrderResult>;

public sealed record OrderResult(Guid Id, string Product, int Quantity);

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, OrderResult>
{
	private readonly List<string>? _log;

	public CreateOrderHandler()
	{
	}

	public CreateOrderHandler(List<string> log) => _log = log;

	public Task<OrderResult> Handle(CreateOrder command, CancellationToken cancellationToken)
	{
		_log?.Add("handler");
		return Task.FromResult(new OrderResult(Guid.NewGuid(), command.Product, command.Quantity));
	}
}

public sealed class ThrowingCreateOrderHandler : ICommandHandler<CreateOrder, OrderResult>
{
	public Task<OrderResult> Handle(CreateOrder command, CancellationToken cancellationToken)
		=> throw new InvalidOperationException("boom-handler");
}

public sealed record GetOrder(Guid Id) : IQuery<OrderResult>;

public sealed class GetOrderHandler : IQueryHandler<GetOrder, OrderResult>
{
	private readonly List<string>? _log;

	public GetOrderHandler()
	{
	}

	public GetOrderHandler(List<string> log) => _log = log;

	public Task<OrderResult> Handle(GetOrder query, CancellationToken cancellationToken)
	{
		_log?.Add("query-handler");
		return Task.FromResult(new OrderResult(query.Id, "cached", 1));
	}
}

public sealed record CancelOrder(Guid Id) : ICommand;

public sealed class HandlerState
{
	public bool VoidExecuted { get; set; }
}

public sealed class CancelOrderHandler : ICommandHandler<CancelOrder>
{
	private readonly HandlerState _state;
	private readonly List<string>? _log;

	public CancelOrderHandler(HandlerState state) => _state = state;

	public CancelOrderHandler(HandlerState state, List<string> log)
	{
		_state = state;
		_log = log;
	}

	public Task Handle(CancelOrder command, CancellationToken cancellationToken)
	{
		_log?.Add("void-handler");
		_state.VoidExecuted = true;
		return Task.CompletedTask;
	}
}

public sealed class RecordingBehavior : IPipelineBehavior<CreateOrder, OrderResult>
{
	private readonly List<string> _log;

	public RecordingBehavior(List<string> log) => _log = log;

	public async Task<OrderResult> Handle(
		CreateOrder request,
		RequestHandlerDelegate<OrderResult> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add("before");
		var response = await next(cancellationToken);
		_log.Add("after");
		return response;
	}
}

public sealed class OrderedBehavior : IPipelineBehavior<CreateOrder, OrderResult>
{
	private readonly string _name;
	private readonly List<string> _log;

	public OrderedBehavior(string name, List<string> log)
	{
		_name = name;
		_log = log;
	}

	public async Task<OrderResult> Handle(
		CreateOrder request,
		RequestHandlerDelegate<OrderResult> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add($"{_name}:before");
		var response = await next(cancellationToken);
		_log.Add($"{_name}:after");
		return response;
	}
}

public sealed class ShortCircuitBehavior : IPipelineBehavior<CreateOrder, OrderResult>
{
	private readonly List<string> _log;

	public ShortCircuitBehavior(List<string> log) => _log = log;

	public Task<OrderResult> Handle(
		CreateOrder request,
		RequestHandlerDelegate<OrderResult> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add("short-circuit");
		return Task.FromResult(new OrderResult(Guid.Empty, "short", 0));
	}
}

public sealed class ThrowBeforeNextBehavior : IPipelineBehavior<CreateOrder, OrderResult>
{
	public Task<OrderResult> Handle(
		CreateOrder request,
		RequestHandlerDelegate<OrderResult> next,
		CancellationToken cancellationToken = default)
		=> throw new InvalidOperationException("boom-before");
}

public sealed class ThrowAfterNextBehavior : IPipelineBehavior<CreateOrder, OrderResult>
{
	private readonly List<string> _log;

	public ThrowAfterNextBehavior(List<string> log) => _log = log;

	public async Task<OrderResult> Handle(
		CreateOrder request,
		RequestHandlerDelegate<OrderResult> next,
		CancellationToken cancellationToken = default)
	{
		_ = await next(cancellationToken);
		_log.Add("after-next");
		throw new InvalidOperationException("boom-after");
	}
}

public sealed class VoidCommandBehavior : IPipelineBehavior<CancelOrder, Unit>
{
	private readonly List<string> _log;

	public VoidCommandBehavior(List<string> log) => _log = log;

	public async Task<Unit> Handle(
		CancelOrder request,
		RequestHandlerDelegate<Unit> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add("void-before");
		var result = await next(cancellationToken);
		_log.Add("void-after");
		return result;
	}
}

public sealed class OpenLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;

	public OpenLoggingBehavior(List<string> log) => _log = log;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add($"open:{typeof(TRequest).Name}");
		return await next(cancellationToken);
	}
}

/// <summary>
/// Probes whether an Activity is already current when the behavior enters/exits next —
/// used to assert UseTelemetry is innermost among AddMediator open behaviors.
/// </summary>
public sealed class ActivityProbeOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;

	public ActivityProbeOpenBehavior(List<string> log) => _log = log;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add(Activity.Current is null ? "probe:enter:no-activity" : "probe:enter:has-activity");
		var response = await next(cancellationToken);
		_log.Add(Activity.Current is null ? "probe:exit:no-activity" : "probe:exit:has-activity");
		return response;
	}
}

public sealed class OpenSecondBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;

	public OpenSecondBehavior(List<string> log) => _log = log;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add($"open2:{typeof(TRequest).Name}");
		return await next(cancellationToken);
	}
}

public sealed class CommandOnlyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;

	public CommandOnlyBehavior(List<string> log) => _log = log;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		if (request is ICommand || request is ICommand<TResponse>)
			_log.Add($"cmd-only:{typeof(TRequest).Name}");
		return await next(cancellationToken);
	}
}

public sealed class QueryOnlyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly List<string> _log;

	public QueryOnlyBehavior(List<string> log) => _log = log;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		if (request is IQuery<TResponse>)
			_log.Add($"query-only:{typeof(TRequest).Name}");
		return await next(cancellationToken);
	}
}

public sealed record TokenProbe : IQuery<bool>;

public sealed class TokenProbeHandler : IQueryHandler<TokenProbe, bool>
{
	public Task<bool> Handle(TokenProbe query, CancellationToken cancellationToken)
		=> Task.FromResult(cancellationToken.IsCancellationRequested);
}

// --- Result pattern fixtures ---

public sealed class Result<T>
{
	public bool IsSuccess { get; init; }
	public T? Value { get; init; }
	public string? Error { get; init; }

	public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
	public static Result<T> Failure(string error) => new() { IsSuccess = false, Error = error };
}

public sealed record PlaceOrder(string Sku) : ICommand<Result<Guid>>;

public sealed class PlaceOrderHandler : ICommandHandler<PlaceOrder, Result<Guid>>
{
	public Task<Result<Guid>> Handle(PlaceOrder command, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(command.Sku))
			return Task.FromResult(Result<Guid>.Failure("sku-required"));
		return Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
	}
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);

public sealed record ListProducts(int Page) : IQuery<Result<PagedResult<string>>>;

public sealed class ListProductsHandler : IQueryHandler<ListProducts, Result<PagedResult<string>>>
{
	public Task<Result<PagedResult<string>>> Handle(ListProducts query, CancellationToken cancellationToken)
		=> Task.FromResult(Result<PagedResult<string>>.Success(
			new PagedResult<string>(new[] { "a", "b" }, 2)));
}

public sealed record ListIds : IQuery<IReadOnlyList<int>>;

public sealed class ListIdsHandler : IQueryHandler<ListIds, IReadOnlyList<int>>
{
	public Task<IReadOnlyList<int>> Handle(ListIds query, CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<int>>(new[] { 1, 2, 3 });
}

public sealed class ResultInspectingBehavior : IPipelineBehavior<PlaceOrder, Result<Guid>>
{
	private readonly List<string> _log;

	public ResultInspectingBehavior(List<string> log) => _log = log;

	public async Task<Result<Guid>> Handle(
		PlaceOrder request,
		RequestHandlerDelegate<Result<Guid>> next,
		CancellationToken cancellationToken = default)
	{
		var result = await next(cancellationToken);
		_log.Add(result.IsSuccess ? "result-ok" : "result-fail");
		return result;
	}
}

/// <summary>Thread-safe enter/exit counters for concurrent pipeline smoke tests.</summary>
public sealed class ConcurrentPipelineCounters
{
	private int _entered;
	private int _exited;
	private int _handlerHits;

	public int Entered => _entered;
	public int Exited => _exited;
	public int HandlerHits => _handlerHits;

	public void MarkEntered() => Interlocked.Increment(ref _entered);
	public void MarkExited() => Interlocked.Increment(ref _exited);
	public void MarkHandler() => Interlocked.Increment(ref _handlerHits);
}

public sealed class ConcurrentCountingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	private readonly ConcurrentPipelineCounters _counters;

	public ConcurrentCountingBehavior(ConcurrentPipelineCounters counters) => _counters = counters;

	public async Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		_counters.MarkEntered();
		try
		{
			await Task.Yield();
			return await next(cancellationToken);
		}
		finally
		{
			_counters.MarkExited();
		}
	}
}

public sealed class ConcurrentOrderLog
{
	private readonly ConcurrentDictionary<Guid, ConcurrentQueue<string>> _steps = new();

	public void Add(Guid correlationId, string step)
	{
		_steps.GetOrAdd(correlationId, static _ => new ConcurrentQueue<string>())
			.Enqueue(step);
	}

	public IReadOnlyList<string> GetSteps(Guid correlationId)
		=> _steps.TryGetValue(correlationId, out var queue)
			? queue.ToArray()
			: Array.Empty<string>();
}

public sealed record ConcurrentCreateOrder(Guid CorrelationId, string Product, int Quantity)
	: ICommand<OrderResult>;

public sealed class ConcurrentCreateOrderHandler : ICommandHandler<ConcurrentCreateOrder, OrderResult>
{
	private readonly ConcurrentOrderLog _log;
	private readonly ConcurrentPipelineCounters? _counters;

	public ConcurrentCreateOrderHandler(ConcurrentOrderLog log, ConcurrentPipelineCounters? counters = null)
	{
		_log = log;
		_counters = counters;
	}

	public async Task<OrderResult> Handle(ConcurrentCreateOrder command, CancellationToken cancellationToken)
	{
		_counters?.MarkHandler();
		_log.Add(command.CorrelationId, "handler");
		await Task.Yield();
		return new OrderResult(command.CorrelationId, command.Product, command.Quantity);
	}
}

public sealed class ConcurrentOrderedBehavior : IPipelineBehavior<ConcurrentCreateOrder, OrderResult>
{
	private readonly string _name;
	private readonly ConcurrentOrderLog _log;

	public ConcurrentOrderedBehavior(string name, ConcurrentOrderLog log)
	{
		_name = name;
		_log = log;
	}

	public async Task<OrderResult> Handle(
		ConcurrentCreateOrder request,
		RequestHandlerDelegate<OrderResult> next,
		CancellationToken cancellationToken = default)
	{
		_log.Add(request.CorrelationId, $"{_name}:before");
		await Task.Yield();
		var response = await next(cancellationToken);
		_log.Add(request.CorrelationId, $"{_name}:after");
		return response;
	}
}

// --- Nested Send (handler injects ISender) ---

public sealed record PlaceAndNotify(string Product, int Quantity) : ICommand<NestedSendResult>;

public sealed record NestedSendResult(OrderResult Order, bool Notified);

public sealed record NotifyOrderCreated(Guid OrderId) : ICommand;

public sealed class NestedSendState
{
	public bool Notified { get; set; }
	public Guid LastNotifiedOrderId { get; set; }
}

public sealed class PlaceAndNotifyHandler : ICommandHandler<PlaceAndNotify, NestedSendResult>
{
	private readonly ISender _sender;
	private readonly List<string>? _log;

	public PlaceAndNotifyHandler(ISender sender) => _sender = sender;

	public PlaceAndNotifyHandler(ISender sender, List<string> log)
	{
		_sender = sender;
		_log = log;
	}

	public async Task<NestedSendResult> Handle(PlaceAndNotify command, CancellationToken cancellationToken)
	{
		_log?.Add("outer-handler");
		var order = await _sender.Send(new CreateOrder(command.Product, command.Quantity), cancellationToken);
		await _sender.Send(new NotifyOrderCreated(order.Id), cancellationToken);
		return new NestedSendResult(order, true);
	}
}

public sealed class NotifyOrderCreatedHandler : ICommandHandler<NotifyOrderCreated>
{
	private readonly NestedSendState _state;
	private readonly List<string>? _log;

	public NotifyOrderCreatedHandler(NestedSendState state) => _state = state;

	public NotifyOrderCreatedHandler(NestedSendState state, List<string> log)
	{
		_state = state;
		_log = log;
	}

	public Task Handle(NotifyOrderCreated command, CancellationToken cancellationToken)
	{
		_log?.Add("inner-void-handler");
		_state.Notified = true;
		_state.LastNotifiedOrderId = command.OrderId;
		return Task.CompletedTask;
	}
}
