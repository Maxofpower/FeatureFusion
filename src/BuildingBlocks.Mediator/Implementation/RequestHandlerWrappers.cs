using BuildingBlocks.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Mediator.Implementation;

internal enum MessageKind
{
	Command,
	VoidCommand,
	Query
}

internal abstract class HandlerWrapper
{
	public abstract Task<object?> HandleObject(
		object message,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken);
}

internal abstract class HandlerWrapper<TResponse> : HandlerWrapper
{
	public abstract Task<TResponse> Handle(
		object message,
		PipelineBehaviorWrapper<TResponse> pipeline,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken);

	public override async Task<object?> HandleObject(
		object message,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		var pipeline = PipelineBehaviorWrapperCache.Get<TResponse>(message.GetType());
		var result = await Handle(message, pipeline, serviceProvider, cancellationToken).ConfigureAwait(false);
		return result;
	}
}

internal sealed class CommandHandlerWrapper<TCommand, TResponse> : HandlerWrapper<TResponse>
	where TCommand : ICommand<TResponse>
{
	public override Task<TResponse> Handle(
		object message,
		PipelineBehaviorWrapper<TResponse> pipeline,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		var handler = ResolveHandler(serviceProvider);
		var command = (TCommand)message;
		return pipeline.Handle(
			command,
			ct => handler.Handle(command, ct),
			serviceProvider,
			cancellationToken);
	}

	private static ICommandHandler<TCommand, TResponse> ResolveHandler(IServiceProvider serviceProvider)
	{
		return HandlerResolution.ResolveClosedOrOpen<ICommandHandler<TCommand, TResponse>>(
			serviceProvider,
			typeof(TCommand),
			"command",
			() =>
			{
				var registry = serviceProvider.GetService<OpenGenericHandlerRegistry>();
				if (registry is null || !registry.HasEntries)
					return Array.Empty<ICommandHandler<TCommand, TResponse>>();

				return registry
					.CreateMatches(serviceProvider, typeof(ICommandHandler<TCommand, TResponse>))
					.Cast<ICommandHandler<TCommand, TResponse>>()
					.ToList();
			});
	}
}

/// <summary>
/// Void commands: resolve <see cref="ICommandHandler{TCommand}"/> and run
/// <see cref="IPipelineBehavior{TRequest,TResponse}"/> as <c>(TCommand, Unit)</c> on the real type.
/// </summary>
internal sealed class VoidCommandHandlerWrapper<TCommand> : HandlerWrapper<Unit>
	where TCommand : ICommand
{
	public override Task<Unit> Handle(
		object message,
		PipelineBehaviorWrapper<Unit> pipeline,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		var handler = ResolveHandler(serviceProvider);
		var command = (TCommand)message;
		return pipeline.Handle(
			command,
			async ct =>
			{
				await handler.Handle(command, ct).ConfigureAwait(false);
				return Unit.Value;
			},
			serviceProvider,
			cancellationToken);
	}

	private static ICommandHandler<TCommand> ResolveHandler(IServiceProvider serviceProvider)
	{
		return HandlerResolution.ResolveClosedOrOpen<ICommandHandler<TCommand>>(
			serviceProvider,
			typeof(TCommand),
			"void command",
			() =>
			{
				var registry = serviceProvider.GetService<OpenGenericHandlerRegistry>();
				if (registry is null || !registry.HasEntries)
					return Array.Empty<ICommandHandler<TCommand>>();

				return registry
					.CreateMatches(serviceProvider, typeof(ICommandHandler<TCommand>))
					.Cast<ICommandHandler<TCommand>>()
					.ToList();
			});
	}
}

internal sealed class QueryHandlerWrapper<TQuery, TResponse> : HandlerWrapper<TResponse>
	where TQuery : IQuery<TResponse>
{
	public override Task<TResponse> Handle(
		object message,
		PipelineBehaviorWrapper<TResponse> pipeline,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		var handler = ResolveHandler(serviceProvider);
		var query = (TQuery)message;
		return pipeline.Handle(
			query,
			ct => handler.Handle(query, ct),
			serviceProvider,
			cancellationToken);
	}

	private static IQueryHandler<TQuery, TResponse> ResolveHandler(IServiceProvider serviceProvider)
	{
		return HandlerResolution.ResolveClosedOrOpen<IQueryHandler<TQuery, TResponse>>(
			serviceProvider,
			typeof(TQuery),
			"query",
			() =>
			{
				var registry = serviceProvider.GetService<OpenGenericHandlerRegistry>();
				if (registry is null || !registry.HasEntries)
					return Array.Empty<IQueryHandler<TQuery, TResponse>>();

				return registry
					.CreateMatches(serviceProvider, typeof(IQueryHandler<TQuery, TResponse>))
					.Cast<IQueryHandler<TQuery, TResponse>>()
					.ToList();
			});
	}
}
