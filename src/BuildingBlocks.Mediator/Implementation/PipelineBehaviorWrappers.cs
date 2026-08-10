using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Mediator.Implementation;

internal abstract class PipelineBehaviorWrapper
{
}

internal abstract class PipelineBehaviorWrapper<TResponse> : PipelineBehaviorWrapper
{
	public abstract Task<TResponse> Handle(
		object message,
		RequestHandlerDelegate<TResponse> next,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken);
}

internal static class PipelineBehaviorWrapperCache
{
	private static readonly ConcurrentDictionary<Type, PipelineBehaviorWrapper> Cache = new();

	public static PipelineBehaviorWrapper<TResponse> Get<TResponse>(Type messageType)
	{
		if (Cache.TryGetValue(messageType, out var cached))
			return (PipelineBehaviorWrapper<TResponse>)cached;

		var wrapperType = typeof(PipelineBehaviorWrapper<,>).MakeGenericType(messageType, typeof(TResponse));
		var created = (PipelineBehaviorWrapper)Activator.CreateInstance(wrapperType)!;
		return (PipelineBehaviorWrapper<TResponse>)Cache.GetOrAdd(messageType, created);
	}
}

/// <summary>
/// Builds the behavior chain. First registered behavior is outermost.
/// </summary>
internal sealed class PipelineBehaviorWrapper<TRequest, TResponse> : PipelineBehaviorWrapper<TResponse>
	where TRequest : notnull
{
	public override Task<TResponse> Handle(
		object message,
		RequestHandlerDelegate<TResponse> next,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
		var pipeline = next;

		foreach (var behavior in behaviors.Reverse())
		{
			var current = pipeline;
			var captured = behavior;
			pipeline = ct => captured.Handle(
				(TRequest)message,
				current,
				ct);
		}

		return pipeline(cancellationToken);
	}
}
