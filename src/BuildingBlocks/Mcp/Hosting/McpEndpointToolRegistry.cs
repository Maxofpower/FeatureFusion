using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Mcp.Hosting;

/// <summary>
/// Collects tools registered via <c>WithMcp</c> on Minimal API endpoints (after DI setup).
/// </summary>
internal sealed class McpEndpointToolRegistry
{
	private readonly List<McpToolDescriptor> _tools = [];
	private readonly object _gate = new();

	public void Add(McpToolDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		lock (_gate)
			_tools.Add(descriptor);
	}

	public IReadOnlyList<McpToolDescriptor> Snapshot()
	{
		lock (_gate)
			return [.. _tools];
	}
}

/// <summary>
/// The mapped <see cref="IEndpointRouteBuilder"/> so the catalog can force endpoint construction (WithMcp conventions).
/// </summary>
internal sealed class McpRouteSourceHolder
{
	public IEndpointRouteBuilder? Routes { get; set; }
}
