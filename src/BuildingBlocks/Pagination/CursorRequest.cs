namespace BuildingBlocks.Pagination;

/// <summary>Incoming page request. No page index — the cursor is the seek position.</summary>
/// <param name="Cursor">Opaque cursor from a previous page; null or empty for first/last.</param>
/// <param name="Limit">Page size (not including the extra row used to detect HasNext).</param>
/// <param name="Direction">Used when <paramref name="Cursor"/> is empty: Forward = first page, Backward = last page.</param>
public sealed record CursorRequest(
	string? Cursor,
	int Limit,
	PageDirection Direction = PageDirection.Forward);
