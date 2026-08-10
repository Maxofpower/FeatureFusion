namespace BuildingBlocks.Mediator;

/// <summary>
/// Sends commands and queries to a single handler through an optional pipeline of behaviors.
/// </summary>
/// <remarks>
/// Prefer this over <see cref="IMediator"/> at call sites for a narrower dependency.
/// CQRS-first: <see cref="ICommand"/> / <see cref="ICommand{TResponse}"/> / <see cref="IQuery{TResponse}"/> —
/// there is no public <c>IRequest</c>.
/// LinkedIn (prior): https://www.linkedin.com/feed/update/urn:li:activity:7311311587372367873/
/// Catalog: docs/linkedin-posts.md (<c>mediator-building-blocks</c>).
/// </remarks>
/// <example>
/// <code>
/// public sealed class OrdersController
/// {
///     private readonly ISender _sender;
///     public OrdersController(ISender sender) =&gt; _sender = sender;
///
///     public Task&lt;OrderResult&gt; Create(CreateOrder command, CancellationToken ct)
///         =&gt; _sender.Send(command, ct);
/// }
/// </code>
/// </example>
public interface ISender
{
	/// <summary>
	/// Sends a void command. Pipeline behaviors are <c>IPipelineBehavior&lt;TCommand, Unit&gt;</c>
	/// on the concrete command type.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
	/// <exception cref="InvalidOperationException">No matching void command handler is registered.</exception>
	Task Send<TCommand>(TCommand command, CancellationToken cancellationToken = default)
		where TCommand : ICommand;

	/// <summary>Sends a command that produces <typeparamref name="TResponse"/>.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
	/// <exception cref="InvalidOperationException">No matching command handler is registered.</exception>
	Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

	/// <summary>Sends a query that produces <typeparamref name="TResponse"/>.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
	/// <exception cref="InvalidOperationException">No matching query handler is registered.</exception>
	Task<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a command or query via runtime type inspection (frameworks / dynamic endpoints).
	/// Prefer typed overloads when the compile-time type is known.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
	/// <exception cref="ArgumentException">Message does not implement a supported command/query interface.</exception>
	/// <exception cref="InvalidOperationException">No matching handler is registered.</exception>
	Task<object?> Send(object message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extends <see cref="ISender"/> with the familiar Mediator name. No Publish/notifications in v1.
/// </summary>
/// <remarks>
/// LinkedIn (prior): https://www.linkedin.com/feed/update/urn:li:activity:7311311587372367873/
/// Catalog: docs/linkedin-posts.md (<c>mediator-building-blocks</c>).
/// </remarks>
public interface IMediator : ISender
{
}

/// <summary>
/// Write-side message with no response payload.
/// Inherits <see cref="ICommand{TResponse}"/> of <see cref="Unit"/> so void commands stay on the real type in the pipeline.
/// </summary>
public interface ICommand : ICommand<Unit>
{
}

/// <summary>Write-side message that returns <typeparamref name="TResponse"/>.</summary>
/// <typeparam name="TResponse">Result type (e.g. id, Result&lt;T&gt;).</typeparam>
public interface ICommand<out TResponse>
{
}

/// <summary>Read-side message that returns <typeparamref name="TResponse"/>. Queries always have a response.</summary>
/// <typeparam name="TResponse">Read model / DTO / Result&lt;T&gt;.</typeparam>
public interface IQuery<out TResponse>
{
}

/// <summary>Handles void <see cref="ICommand"/> (returns no payload; pipeline uses <see cref="Unit"/>).</summary>
public interface ICommandHandler<in TCommand>
	where TCommand : ICommand
{
	/// <summary>Handles the command.</summary>
	Task Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles <see cref="ICommand{TResponse}"/>.</summary>
public interface ICommandHandler<in TCommand, TResponse>
	where TCommand : ICommand<TResponse>
{
	/// <summary>Handles the command.</summary>
	Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Handles <see cref="IQuery{TResponse}"/>.</summary>
public interface IQueryHandler<in TQuery, TResponse>
	where TQuery : IQuery<TResponse>
{
	/// <summary>Handles the query.</summary>
	Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Continuation delegate for the remainder of the pipeline (next behavior or the handler).
/// </summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken = default);

/// <summary>
/// Cross-cutting pipeline behavior around a command or query handler.
/// </summary>
/// <remarks>
/// Registration order: the first registered behavior is outermost (or use explicit <c>order</c> on
/// <c>AddOpenBehavior</c>). Register open generics via <c>AddOpenBehavior(typeof(MyBehavior&lt;,&gt;))</c>.
/// Void commands use <c>IPipelineBehavior&lt;TCommand, Unit&gt;</c> on the concrete command type.
/// </remarks>
/// <example>
/// <code>
/// public sealed class LoggingBehavior&lt;TRequest, TResponse&gt; : IPipelineBehavior&lt;TRequest, TResponse&gt;
///     where TRequest : notnull
/// {
///     public async Task&lt;TResponse&gt; Handle(
///         TRequest request, RequestHandlerDelegate&lt;TResponse&gt; next, CancellationToken ct = default)
///     {
///         // before
///         var response = await next(ct);
///         // after
///         return response;
///     }
/// }
/// </code>
/// </example>
public interface IPipelineBehavior<in TRequest, TResponse>
	where TRequest : notnull
{
	/// <summary>Invokes the behavior, then <paramref name="next"/>.</summary>
	Task<TResponse> Handle(
		TRequest request,
		RequestHandlerDelegate<TResponse> next,
		CancellationToken cancellationToken = default);
}
