namespace BuildingBlocks.Mediator.Pipeline;

/// <summary>
/// Helpers to classify mediator messages for host pipeline filters.
/// </summary>
public static class MessageKind
{
	/// <summary>
	/// Returns true when <paramref name="request"/> implements <see cref="ICommand"/>
	/// or a closed <see cref="ICommand{TResponse}"/>.
	/// </summary>
	public static bool IsCommand(object request)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request is ICommand)
			return true;

		var type = request.GetType();
		return type.GetInterfaces()
			.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
	}

	/// <summary>
	/// Returns true when <paramref name="request"/> implements a closed <see cref="IQuery{TResponse}"/>.
	/// </summary>
	public static bool IsQuery(object request)
	{
		ArgumentNullException.ThrowIfNull(request);

		var type = request.GetType();
		return type.GetInterfaces()
			.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
	}
}
