namespace BuildingBlocks.Pagination;

/// <summary>
/// Typed pagination error. Hosts map <see cref="Code"/> to HTTP 400 (or equivalent).
/// </summary>
public sealed class PaginationException : Exception
{
	/// <summary>Creates an exception with an error code and message.</summary>
	/// <param name="code">Error code.</param>
	/// <param name="message">Message.</param>
	public PaginationException(PaginationErrorCode code, string message)
		: base(message)
	{
		Code = code;
	}

	/// <summary>Creates an exception with an error code, message, and inner exception.</summary>
	/// <param name="code">Error code.</param>
	/// <param name="message">Message.</param>
	/// <param name="inner">Inner exception.</param>
	public PaginationException(PaginationErrorCode code, string message, Exception inner)
		: base(message, inner)
	{
		Code = code;
	}

	/// <summary>Stable error code.</summary>
	public PaginationErrorCode Code { get; }
}
