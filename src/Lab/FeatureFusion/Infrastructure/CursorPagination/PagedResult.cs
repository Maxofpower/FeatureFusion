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
}
