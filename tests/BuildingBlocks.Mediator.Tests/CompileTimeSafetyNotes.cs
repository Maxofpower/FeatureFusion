namespace BuildingBlocks.Mediator.Tests;

/// <summary>
/// Compile-time CQRS safety (documented; not runtime tests):
/// - Queries must be <c>IQuery&lt;TResponse&gt;</c> — there is no non-generic <c>IQuery</c>.
/// - Void writes use <c>ICommand</c> only.
/// - Wrong handler interface fails at compile time (e.g. <c>IQueryHandler</c> for an <c>ICommand</c>).
/// </summary>
internal static class CompileTimeSafetyNotes
{
}
