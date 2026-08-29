namespace BuildingBlocks.Mcp;

/// <summary>
/// Ambient <see cref="McpInvokeContext"/> for the current tool invoke (Mediator handlers can read <see cref="McpInvokeContext.DryRun"/>).
/// </summary>
public interface IMcpInvokeContextAccessor
{
	/// <summary>Context for the in-flight invoke, or null outside an invoke.</summary>
	McpInvokeContext? Current { get; }
}

/// <summary>
/// AsyncLocal implementation of <see cref="IMcpInvokeContextAccessor"/>.
/// </summary>
public sealed class McpInvokeContextAccessor : IMcpInvokeContextAccessor
{
	private static readonly AsyncLocal<McpInvokeContext?> CurrentValue = new();

	/// <inheritdoc />
	public McpInvokeContext? Current => CurrentValue.Value;

	/// <summary>
	/// Sets <see cref="Current"/> until the returned scope is disposed.
	/// </summary>
	public IDisposable Push(McpInvokeContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		var previous = CurrentValue.Value;
		CurrentValue.Value = context;
		return new Pop(previous);
	}

	private sealed class Pop : IDisposable
	{
		private readonly McpInvokeContext? _previous;

		public Pop(McpInvokeContext? previous) => _previous = previous;

		public void Dispose() => CurrentValue.Value = _previous;
	}
}
