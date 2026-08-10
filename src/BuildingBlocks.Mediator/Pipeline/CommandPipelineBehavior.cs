namespace BuildingBlocks.Mediator.Pipeline;

/// <summary>
/// Pipeline behavior that runs only for command requests (<see cref="ICommand"/> / <see cref="ICommand{TResponse}"/>).
/// Queries skip straight to the next delegate without invoking <see cref="HandleCommand"/>.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type (use <see cref="Unit"/> for void commands).</typeparam>
/// <example>
/// <code>
/// public sealed class MetricsOnCommands&lt;TRequest, TResponse&gt; : CommandPipelineBehavior&lt;TRequest, TResponse&gt;
///     where TRequest : notnull
/// {
///     protected override async Task&lt;TResponse&gt; HandleCommand(
///         TRequest request, RequestHandlerDelegate&lt;TResponse&gt; next, CancellationToken cancellationToken)
///     {
///         // metrics...
///         return await next(cancellationToken);
///     }
/// }
/// </code>
/// </example>
public abstract class CommandPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	/// <inheritdoc />
	public Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(next);

		if (!MessageKind.IsCommand(request))
			return next(cancellationToken);

		return HandleCommand(request, next, cancellationToken);
	}

	/// <summary>Invoked only when <typeparamref name="TRequest"/> is a command.</summary>
	protected abstract Task<TResponse> HandleCommand(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken);
}
