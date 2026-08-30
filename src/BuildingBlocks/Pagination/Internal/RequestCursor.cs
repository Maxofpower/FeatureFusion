namespace BuildingBlocks.Pagination;

internal static class RequestCursor
{
	public static (bool WalkBackward, object?[]? Values, bool FromCursor) Resolve<T>(
		CursorRequest request,
		SortKey<T> sortKey,
		PaginationOptions options)
	{
		options.ValidateLimit(request.Limit);
		options.ValidateSigning();

		if (CursorCodec.IsEmpty(request.Cursor))
		{
			return (request.Direction == PageDirection.Backward, null, false);
		}

		var decoded = CursorCodec.Decode(request.Cursor!, sortKey, options);
		return (decoded.Walk == PageDirection.Backward, decoded.Values, true);
	}
}
