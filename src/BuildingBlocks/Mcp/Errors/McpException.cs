namespace BuildingBlocks.Mcp;

/// <summary>
/// Base exception for BuildingBlocks.Mcp. Prefer <see cref="McpResult{T}"/> for domain failures.
/// </summary>
public abstract class McpException : Exception
{
	/// <summary>Creates the exception with a message.</summary>
	protected McpException(string message) : base(message)
	{
	}

	/// <summary>Creates the exception with a message and inner exception.</summary>
	protected McpException(string message, Exception innerException) : base(message, innerException)
	{
	}
}

/// <summary>
/// Thrown at startup when the tool catalog is invalid. The host must not listen until this is fixed.
/// </summary>
public sealed class McpCatalogException : McpException
{
	/// <summary>Creates a catalog exception.</summary>
	public McpCatalogException(string message) : base(message)
	{
	}
}

/// <summary>
/// Thrown when invocation cannot start (missing dispatcher). Not used for domain errors.
/// </summary>
public sealed class McpInvocationException : McpException
{
	/// <summary>Creates an invocation exception.</summary>
	public McpInvocationException(string message) : base(message)
	{
	}
}

/// <summary>
/// Thrown when a linked timeout cancels the invoke. Mapped to <see cref="McpErrorCode.Timeout"/>.
/// </summary>
public sealed class McpTimeoutException : McpException
{
	/// <summary>Creates a timeout exception.</summary>
	public McpTimeoutException(string toolName, TimeSpan timeout)
		: base($"Tool '{toolName}' timed out after {timeout.TotalMilliseconds:0} ms.")
	{
		ToolName = toolName;
		Timeout = timeout;
	}

	/// <summary>Tool that timed out.</summary>
	public string ToolName { get; }

	/// <summary>Configured timeout.</summary>
	public TimeSpan Timeout { get; }
}

/// <summary>
/// Maps <see cref="OperationCanceledException"/> when the caller canceled (not a timeout).
/// </summary>
public sealed class McpCanceledException : McpException
{
	/// <summary>Creates a canceled exception.</summary>
	public McpCanceledException() : base("The MCP tool call was canceled.")
	{
	}

	/// <summary>Creates a canceled exception with an inner exception.</summary>
	public McpCanceledException(Exception innerException) : base("The MCP tool call was canceled.", innerException)
	{
	}
}
