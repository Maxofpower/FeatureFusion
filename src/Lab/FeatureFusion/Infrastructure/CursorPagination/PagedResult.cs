namespace FeatureFusion.Infrastructure.CursorPagination
{
	public record PagedResult<T>(
	IReadOnlyList<T> Items,
	string NextCursor,
	string PreviousCursor,
	bool HasMore,
	bool HasPrevious,
	int TotalCount)
	{
		public static PagedResult<T> Empty { get; } = new(
			[],
			string.Empty,
			string.Empty,
			false,
			false,
			0);
	}

	public static class PagedResultMapper
	{
		public static PagedResult<T> ToPagedResult<T>(this BuildingBlocks.Pagination.CursorPage<T> page)
			=> new(
				page.Items,
				page.Next ?? string.Empty,
				page.Previous ?? string.Empty,
				page.HasNext,
				page.HasPrevious,
				page.TotalCount ?? 0);
	}
}
