using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Mediator.DependencyInjection;

/// <summary>
/// Snapshot of closed handler registration counts taken at <c>AddMediator</c> time.
/// Used to detect ambiguous handlers without calling <c>GetServices</c> (which instantiates every registration).
/// </summary>
internal sealed class HandlerCardinalityIndex
{
	private readonly Dictionary<Type, int> _counts;

	public HandlerCardinalityIndex(IReadOnlyDictionary<Type, int> counts)
	{
		_counts = new Dictionary<Type, int>(counts);
	}

	public int CountOf(Type handlerServiceType)
		=> _counts.TryGetValue(handlerServiceType, out var count) ? count : 0;

	public static HandlerCardinalityIndex Create(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		var counts = new Dictionary<Type, int>();
		foreach (var descriptor in services)
		{
			if (!IsClosedHandlerServiceType(descriptor.ServiceType))
				continue;

			counts.TryGetValue(descriptor.ServiceType, out var current);
			counts[descriptor.ServiceType] = current + 1;
		}

		return new HandlerCardinalityIndex(counts);
	}

	private static bool IsClosedHandlerServiceType(Type serviceType)
	{
		if (!serviceType.IsGenericType || serviceType.IsGenericTypeDefinition)
			return false;

		var definition = serviceType.GetGenericTypeDefinition();
		return definition == typeof(ICommandHandler<>)
		       || definition == typeof(ICommandHandler<,>)
		       || definition == typeof(IQueryHandler<,>);
	}
}
