namespace BuildingBlocks.Pagination;

/// <summary>Host options for a page request.</summary>
public sealed class PaginationOptions
{
	/// <summary>Maximum allowed <see cref="CursorRequest.Limit"/> (default 100).</summary>
	public int MaxLimit { get; set; } = 100;

	/// <summary>When true, adapters run a count on the unpaginated query.</summary>
	public bool IncludeTotalCount { get; set; }

	/// <summary>
	/// Optional HMAC-SHA256 key. Set this on public HTTP APIs so clients cannot forge
	/// <c>Walk</c> or key values. When set, unsigned or tampered cursors are rejected.
	/// Empty array is invalid. Omit for trusted internal callers.
	/// </summary>
	public byte[]? SigningKey { get; set; }

	/// <summary>
	/// Seek-side null placement for reference types (default last). Does not change
	/// <c>ORDER BY</c> nulls. Nullable value-type slots are rejected.
	/// </summary>
	public NullOrder Nulls { get; set; } = NullOrder.Last;

	/// <summary>
	/// Optional SQL Server dirty-read hint. Default <see cref="QueryHint.None"/> — zero extra SQL.
	/// <see cref="QueryHint.ReadUncommitted"/> is session isolation, not <c>WITH (NOLOCK)</c>.
	/// EF starts one SQL Server <c>ReadUncommitted</c> transaction around COUNT (if requested)
	/// and PAGE when there is no ambient transaction. An ambient transaction is ignored (no nest).
	/// After the operation, the still-open connection is restored to <c>READ COMMITTED</c>.
	/// PostgreSQL and Sqlite are a no-op. Dapper prefixes <c>SET TRANSACTION ISOLATION LEVEL</c>
	/// on SQL Server only, then restores <c>READ COMMITTED</c> on the open connection.
	/// </summary>
	public QueryHint Hint { get; set; } = QueryHint.None;

	/// <summary>
	/// Default options. Returns a <strong>new</strong> instance so hosts cannot mutate a shared singleton
	/// from another thread.
	/// </summary>
	public static PaginationOptions Default => new();

	internal void ValidateSigning()
	{
		if (SigningKey is { Length: 0 })
		{
			throw new PaginationException(
				PaginationErrorCode.SigningKeyRequired,
				"PaginationOptions.SigningKey is empty. Omit the property or provide a non-empty key.");
		}
	}

	internal void ValidateLimit(int limit)
	{
		if (limit < 1 || limit > MaxLimit)
		{
			throw new PaginationException(
				PaginationErrorCode.InvalidLimit,
				$"Limit must be between 1 and {MaxLimit}.");
		}
	}
}
