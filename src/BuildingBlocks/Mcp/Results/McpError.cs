namespace BuildingBlocks.Mcp;

/// <summary>
/// Structured error payload serialized to the MCP client when <see cref="McpResult{T}.IsSuccess"/> is false.
/// </summary>
public sealed class McpError
{
	/// <summary>
	/// Creates an error with a code and message.
	/// </summary>
	/// <param name="code">Stable error code.</param>
	/// <param name="message">Human-readable message. Must not include secrets or stack traces.</param>
	/// <param name="details">Optional property-level details.</param>
	/// <param name="httpStatusHint">Optional HTTP status for hosts that map MCP to HTTP.</param>
	/// <param name="retryAfterSeconds">Optional retry-after hint for <see cref="McpErrorCode.RateLimited"/>.</param>
	public McpError(
		McpErrorCode code,
		string message,
		IReadOnlyDictionary<string, string[]>? details = null,
		int? httpStatusHint = null,
		int? retryAfterSeconds = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);
		Code = code;
		Message = message;
		Details = details;
		HttpStatusHint = httpStatusHint;
		RetryAfterSeconds = retryAfterSeconds;
	}

	/// <summary>Stable error code.</summary>
	public McpErrorCode Code { get; }

	/// <summary>Human-readable message.</summary>
	public string Message { get; }

	/// <summary>Optional property-level details.</summary>
	public IReadOnlyDictionary<string, string[]>? Details { get; }

	/// <summary>Optional HTTP status hint.</summary>
	public int? HttpStatusHint { get; }

	/// <summary>Optional retry-after seconds for rate limits.</summary>
	public int? RetryAfterSeconds { get; }
}
