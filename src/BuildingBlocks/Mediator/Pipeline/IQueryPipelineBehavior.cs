namespace BuildingBlocks.Mediator.Pipeline;

/// <summary>
/// Pipeline behavior that applies only to queries.
/// </summary>
/// <remarks>
/// Prefer this over <see cref="QueryPipelineBehavior{TRequest,TResponse}"/> when registering an
/// open generic via <c>AddOpenBehavior</c> / <c>AddOpenQueryBehavior</c>. The
/// <see cref="IQuery{TResponse}"/> constraint means MS.DI will not close (or construct) the type
/// for commands. The 1.0 filter base still works: it is constructed for every Send and skips commands
/// at runtime.
/// </remarks>
/// <typeparam name="TQuery">Query type.</typeparam>
/// <typeparam name="TResponse">Read model / DTO / Result&lt;T&gt;.</typeparam>
/// <example>
/// <code>
/// public sealed class CacheQueries&lt;TQuery, TResponse&gt; : IQueryPipelineBehavior&lt;TQuery, TResponse&gt;
///     where TQuery : IQuery&lt;TResponse&gt;
/// {
///     public async Task&lt;TResponse&gt; Handle(
///         TQuery query, RequestHandlerDelegate&lt;TResponse&gt; next, CancellationToken ct)
///         =&gt; await next(ct);
/// }
///
/// cfg.AddOpenBehavior(typeof(CacheQueries&lt;,&gt;), order: 20);
/// // or cfg.AddOpenQueryBehavior(typeof(CacheQueries&lt;,&gt;), order: 20);
/// </code>
/// </example>
public interface IQueryPipelineBehavior<in TQuery, TResponse> : IPipelineBehavior<TQuery, TResponse>
	where TQuery : IQuery<TResponse>
{
}
