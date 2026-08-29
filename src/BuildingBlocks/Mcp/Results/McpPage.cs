namespace BuildingBlocks.Mcp;

/// <summary>
/// Standard paged tool output so agents request the next page instead of dumping an entire collection.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Page of items.</param>
/// <param name="NextCursor">Opaque cursor for the next page; null or empty when there is no next page.</param>
public sealed record McpPage<T>(IReadOnlyList<T> Items, string? NextCursor)
{
	/// <summary>
	/// Builds a page from items and an optional cursor.
	/// </summary>
	/// <param name="items">Items (null treated as empty).</param>
	/// <param name="nextCursor">Next cursor.</param>
	public static McpPage<T> From(IEnumerable<T>? items, string? nextCursor)
		=> new(items is IReadOnlyList<T> list ? list : items?.ToList() ?? [], nextCursor);
}
