namespace BuildingBlocks.Pagination;

/// <summary>One page of keyset results.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Page items in sort-key order.</param>
/// <param name="Next">Cursor for the next page; null when <see cref="HasNext"/> is false.</param>
/// <param name="Previous">Cursor for the previous page; null when <see cref="HasPrevious"/> is false.</param>
/// <param name="HasNext">True when more rows exist after this page.</param>
/// <param name="HasPrevious">True when more rows exist before this page.</param>
/// <param name="TotalCount">Set only when <see cref="PaginationOptions.IncludeTotalCount"/> is true.</param>
public sealed record CursorPage<T>(
	IReadOnlyList<T> Items,
	string? Next,
	string? Previous,
	bool HasNext,
	bool HasPrevious,
	int? TotalCount = null)
{
	/// <summary>Empty page.</summary>
	public static CursorPage<T> Empty { get; } = new([], null, null, false, false, null);
}
