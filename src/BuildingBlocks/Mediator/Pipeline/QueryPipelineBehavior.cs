namespace BuildingBlocks.Mediator.Pipeline;

/// <summary>
/// Pipeline behavior that runs only for query requests (<see cref="IQuery{TResponse}"/>).
/// Commands skip straight to the next delegate without invoking <see cref="HandleQuery"/>.
/// </summary>
/// <remarks>
/// Prefer <see cref="IQueryPipelineBehavior{TQuery,TResponse}"/> for new open generics so MS.DI
/// does not construct this type for commands. This base remains supported (1.0.1 contract): it is
/// unconstrained and skips the opposite kind at runtime.
/// </remarks>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
/// <example>
/// <code>
/// public sealed class CacheQueries&lt;TRequest, TResponse&gt; : QueryPipelineBehavior&lt;TRequest, TResponse&gt;
///     where TRequest : notnull
/// {
///     protected override async Task&lt;TResponse&gt; HandleQuery(
///         TRequest request, RequestHandlerDelegate&lt;TResponse&gt; next, CancellationToken cancellationToken)
///     {
///         // cache lookup...
///         return await next(cancellationToken);
///     }
/// }
/// </code>
/// </example>
public abstract class QueryPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
	where TRequest : notnull
{
	/// <inheritdoc />
	public Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(next);

		if (!MessageKind.IsQuery(request))
			return next(cancellationToken);

		return HandleQuery(request, next, cancellationToken);
	}

	/// <summary>Invoked only when <typeparamref name="TRequest"/> is a query.</summary>
	protected abstract Task<TResponse> HandleQuery(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken);
}
