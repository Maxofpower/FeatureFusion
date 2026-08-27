using BuildingBlocks.Mediator.Implementation;
using BuildingBlocks.Mediator.Telemetry;
using System.Collections.Concurrent;

namespace BuildingBlocks.Mediator;

/// <summary>
/// Default <see cref="IMediator"/> / <see cref="ISender"/> with cached command/query and pipeline wrappers.
/// </summary>
/// <remarks>
/// Wrapper caches are process-wide and keyed by message <see cref="Type"/> (and kind) for hot-path Send performance.
/// Optional <see cref="MediatorSendTelemetry"/> wraps the entire pipeline + handler (not a pipeline behavior).
/// </remarks>
public sealed class Mediator : IMediator
{
	private readonly IServiceProvider _serviceProvider;
	private readonly MediatorSendTelemetry? _telemetry;
	private static readonly ConcurrentDictionary<(Type MessageType, MessageKind Kind), HandlerWrapper> Handlers = new();

	/// <summary>Creates a mediator bound to <paramref name="serviceProvider"/>.</summary>
	public Mediator(IServiceProvider serviceProvider)
		: this(serviceProvider, telemetry: null)
	{
	}

	/// <summary>
	/// Creates a mediator with optional Send telemetry. When <paramref name="telemetry"/> is null,
	/// no ActivitySource enrichment runs.
	/// </summary>
	public Mediator(IServiceProvider serviceProvider, MediatorSendTelemetry? telemetry)
	{
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		_telemetry = telemetry;
	}

	/// <inheritdoc />
	public async Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
		where TCommand : ICommand
	{
		ArgumentNullException.ThrowIfNull(command);
		_ = await Dispatch<Unit>(command, command.GetType(), MessageKind.VoidCommand, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <inheritdoc />
	public Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		// Void commands also implement ICommand<Unit>; prefer the void handler path so
		// ICommandHandler<T> + IPipelineBehavior<T, Unit> bind to the concrete type.
		if (typeof(TResponse) == typeof(Unit) && command is ICommand voidCommand)
		{
			return SendVoidAsTypedResponse<TResponse>(voidCommand, cancellationToken);
		}

		return Dispatch<TResponse>(command, command.GetType(), MessageKind.Command, cancellationToken);
	}

	/// <inheritdoc />
	public Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return Dispatch<TResponse>(query, query.GetType(), MessageKind.Query, cancellationToken);
	}

	/// <inheritdoc />
	public Task<object?> Send(object message, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(message);

		var messageType = message.GetType();

		if (message is ICommand)
		{
			var wrapper = GetHandler(messageType, MessageKind.VoidCommand);
			return TraceObject(message, messageType, MessageKind.VoidCommand,
				ct => wrapper.HandleObject(message, _serviceProvider, ct), cancellationToken);
		}

		var commandInterface = messageType.GetInterfaces()
			.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
		if (commandInterface is not null)
		{
			var responseType = commandInterface.GetGenericArguments()[0];
			var wrapperType = typeof(CommandHandlerWrapper<,>).MakeGenericType(messageType, responseType);
			var wrapper = (HandlerWrapper)Handlers.GetOrAdd(
				(messageType, MessageKind.Command),
				_ => (HandlerWrapper)Activator.CreateInstance(wrapperType)!);
			return TraceObject(message, messageType, MessageKind.Command,
				ct => wrapper.HandleObject(message, _serviceProvider, ct), cancellationToken);
		}

		var queryInterface = messageType.GetInterfaces()
			.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
		if (queryInterface is not null)
		{
			var responseType = queryInterface.GetGenericArguments()[0];
			var wrapperType = typeof(QueryHandlerWrapper<,>).MakeGenericType(messageType, responseType);
			var wrapper = (HandlerWrapper)Handlers.GetOrAdd(
				(messageType, MessageKind.Query),
				_ => (HandlerWrapper)Activator.CreateInstance(wrapperType)!);
			return TraceObject(message, messageType, MessageKind.Query,
				ct => wrapper.HandleObject(message, _serviceProvider, ct), cancellationToken);
		}

		throw new ArgumentException(
			$"{messageType.Name} does not implement ICommand, ICommand<> or IQuery<>.",
			nameof(message));
	}

	private async Task<TResponse> SendVoidAsTypedResponse<TResponse>(ICommand command, CancellationToken cancellationToken)
	{
		var unit = await Dispatch<Unit>(command, command.GetType(), MessageKind.VoidCommand, cancellationToken)
			.ConfigureAwait(false);
		return (TResponse)(object)unit;
	}

	private Task<TResponse> Dispatch<TResponse>(
		object message,
		Type messageType,
		MessageKind kind,
		CancellationToken cancellationToken)
	{
		var handler = GetHandler<TResponse>(messageType, kind);
		var pipeline = PipelineBehaviorWrapperCache.Get<TResponse>(messageType);

		Task<TResponse> SendCore(CancellationToken ct)
			=> handler.Handle(message, pipeline, _serviceProvider, ct);

		if (_telemetry is null)
			return SendCore(cancellationToken);

		return _telemetry.TraceAsync(messageType, ToKindTag(kind), SendCore, cancellationToken);
	}

	private Task<object?> TraceObject(
		object message,
		Type messageType,
		MessageKind kind,
		Func<CancellationToken, Task<object?>> send,
		CancellationToken cancellationToken)
	{
		if (_telemetry is null)
			return send(cancellationToken);

		return _telemetry.TraceAsync(messageType, ToKindTag(kind), send, cancellationToken);
	}

	private static string ToKindTag(MessageKind kind) => kind switch
	{
		MessageKind.VoidCommand => "void-command",
		MessageKind.Command => "command",
		MessageKind.Query => "query",
		_ => "message"
	};

	private static HandlerWrapper GetHandler(Type messageType, MessageKind kind)
	{
		var key = (messageType, kind);
		if (Handlers.TryGetValue(key, out var cached))
			return cached;

		Type wrapperType = kind switch
		{
			MessageKind.VoidCommand => typeof(VoidCommandHandlerWrapper<>).MakeGenericType(messageType),
			MessageKind.Command => throw new InvalidOperationException("Command wrapper requires response type."),
			MessageKind.Query => throw new InvalidOperationException("Query wrapper requires response type."),
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};

		var created = (HandlerWrapper)Activator.CreateInstance(wrapperType)!;
		return Handlers.GetOrAdd(key, created);
	}

	private static HandlerWrapper<TResponse> GetHandler<TResponse>(Type messageType, MessageKind kind)
	{
		var key = (messageType, kind);
		if (Handlers.TryGetValue(key, out var cached))
			return (HandlerWrapper<TResponse>)cached;

		var wrapperType = kind switch
		{
			MessageKind.VoidCommand => typeof(VoidCommandHandlerWrapper<>).MakeGenericType(messageType),
			MessageKind.Command => typeof(CommandHandlerWrapper<,>).MakeGenericType(messageType, typeof(TResponse)),
			MessageKind.Query => typeof(QueryHandlerWrapper<,>).MakeGenericType(messageType, typeof(TResponse)),
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};

		var created = (HandlerWrapper)Activator.CreateInstance(wrapperType)!;
		return (HandlerWrapper<TResponse>)Handlers.GetOrAdd(key, created);
	}
}
