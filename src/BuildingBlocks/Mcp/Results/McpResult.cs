namespace BuildingBlocks.Mcp;

/// <summary>
/// Outcome of one MCP tool invocation. Prefer this over throwing for validation and domain errors.
/// </summary>
/// <typeparam name="T">Success payload serialized to the client.</typeparam>
public readonly struct McpResult<T>
{
	private McpResult(bool isSuccess, T? value, McpError? error)
	{
		IsSuccess = isSuccess;
		Value = value;
		Error = error;
	}

	/// <summary>True when <see cref="Value"/> is present.</summary>
	public bool IsSuccess { get; }

	/// <summary>Success payload; default when failed.</summary>
	public T? Value { get; }

	/// <summary>Failure payload; null when successful.</summary>
	public McpError? Error { get; }

	/// <summary>Creates a successful result.</summary>
	/// <param name="value">Payload to serialize.</param>
	public static McpResult<T> Ok(T value) => new(true, value, null);

	/// <summary>Creates a failed result.</summary>
	/// <param name="error">Structured error.</param>
	/// <exception cref="ArgumentNullException"><paramref name="error"/> is null.</exception>
	public static McpResult<T> Fail(McpError error)
	{
		ArgumentNullException.ThrowIfNull(error);
		return new(false, default, error);
	}

	/// <summary>Creates a failed result from code and message.</summary>
	public static McpResult<T> Fail(McpErrorCode code, string message)
		=> Fail(new McpError(code, message));
}

/// <summary>
/// Helpers for constructing <see cref="McpResult{T}"/> without repeating the type argument at the call site.
/// </summary>
public static class McpResult
{
	/// <summary>Creates a successful result.</summary>
	public static McpResult<T> Ok<T>(T value) => McpResult<T>.Ok(value);

	/// <summary>Creates a failed result.</summary>
	public static McpResult<T> Fail<T>(McpError error) => McpResult<T>.Fail(error);

	/// <summary>Creates a failed result from code and message.</summary>
	public static McpResult<T> Fail<T>(McpErrorCode code, string message) => McpResult<T>.Fail(code, message);
}
