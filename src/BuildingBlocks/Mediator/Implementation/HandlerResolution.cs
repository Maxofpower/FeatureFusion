using BuildingBlocks.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Mediator.Implementation;

/// <summary>
/// Ensures exactly one handler is selected for a message type.
/// </summary>
internal static class HandlerResolution
{
	public static THandler ResolveClosedOrOpen<THandler>(
		IServiceProvider serviceProvider,
		Type messageType,
		string kindLabel,
		Func<IReadOnlyList<THandler>> openGenericFallback)
		where THandler : class
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);
		ArgumentNullException.ThrowIfNull(messageType);
		ArgumentNullException.ThrowIfNull(openGenericFallback);

		var handlerType = typeof(THandler);
		var index = serviceProvider.GetService<HandlerCardinalityIndex>();

		if (index is not null)
		{
			var count = index.CountOf(handlerType);
			if (count > 1)
			{
				throw new InvalidOperationException(
					$"Multiple handlers registered for {kindLabel} '{messageType.FullName}'; expected exactly one.");
			}

			// Prefer any closed DI registration (including those added after AddMediator) over open-generic.
			var closed = serviceProvider.GetService<THandler>();
			if (closed is not null)
				return closed;

			return ResolveOpenOnly(openGenericFallback, messageType, kindLabel);
		}

		// Manual Mediator registration without AddMediator index — enumerate constructible handlers.
		var closedHandlers = serviceProvider.GetServices<THandler>() as IList<THandler>
		             ?? serviceProvider.GetServices<THandler>().ToList();
		if (closedHandlers.Count == 1)
			return closedHandlers[0];
		if (closedHandlers.Count > 1)
		{
			throw new InvalidOperationException(
				$"Multiple handlers registered for {kindLabel} '{messageType.FullName}'; expected exactly one.");
		}

		return ResolveOpenOnly(openGenericFallback, messageType, kindLabel);
	}

	private static THandler ResolveOpenOnly<THandler>(
		Func<IReadOnlyList<THandler>> openGenericFallback,
		Type messageType,
		string kindLabel)
		where THandler : class
	{
		var open = openGenericFallback();
		if (open.Count == 1)
			return open[0];

		if (open.Count > 1)
		{
			throw new InvalidOperationException(
				$"Multiple open-generic handlers match {kindLabel} '{messageType.FullName}'; expected exactly one.");
		}

		throw new InvalidOperationException(
			$"No {kindLabel} handler registered for '{messageType.FullName}'.");
	}
}
