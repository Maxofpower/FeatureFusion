namespace BuildingBlocks.Mediator.Pipeline;

/// <summary>
/// Pipeline behavior that applies only to commands.
/// </summary>
/// <remarks>
/// Prefer this over <see cref="CommandPipelineBehavior{TRequest,TResponse}"/> when registering an
/// open generic via <c>AddOpenBehavior</c> / <c>AddOpenCommandBehavior</c>. The
/// <see cref="ICommand{TResponse}"/> constraint means MS.DI will not close (or construct) the type
/// for queries. The 1.0 filter base still works: it is constructed for every Send and skips queries
/// at runtime.
/// </remarks>
/// <typeparam name="TCommand">Command type.</typeparam>
/// <typeparam name="TResponse">Response type (use <see cref="Unit"/> for void commands).</typeparam>
/// <example>
/// <code>
/// public sealed class AuditCommands&lt;TCommand, TResponse&gt; : ICommandPipelineBehavior&lt;TCommand, TResponse&gt;
///     where TCommand : ICommand&lt;TResponse&gt;
/// {
///     public async Task&lt;TResponse&gt; Handle(
///         TCommand command, RequestHandlerDelegate&lt;TResponse&gt; next, CancellationToken ct)
///         =&gt; await next(ct);
/// }
///
/// cfg.AddOpenBehavior(typeof(AuditCommands&lt;,&gt;), order: 10);
/// // or cfg.AddOpenCommandBehavior(typeof(AuditCommands&lt;,&gt;), order: 10);
/// </code>
/// </example>
public interface ICommandPipelineBehavior<in TCommand, TResponse> : IPipelineBehavior<TCommand, TResponse>
	where TCommand : ICommand<TResponse>
{
}
