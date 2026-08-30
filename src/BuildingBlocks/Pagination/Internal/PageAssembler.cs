namespace BuildingBlocks.Pagination;

internal static class PageAssembler
{
	public static CursorPage<T> Assemble<T>(
		List<T> fetched,
		List<object?[]> keys,
		SortKey<T> sortKey,
		int limit,
		bool walkedBackward,
		bool fromCursor,
		PaginationOptions options,
		int? totalCount)
	{
		var extra = fetched.Count > limit;
		if (walkedBackward)
		{
			fetched.Reverse();
			keys.Reverse();
		}

		if (extra)
		{
			if (walkedBackward)
			{
				fetched.RemoveAt(0);
				keys.RemoveAt(0);
			}
			else
			{
				fetched.RemoveAt(fetched.Count - 1);
				keys.RemoveAt(keys.Count - 1);
			}
		}

		bool hasNext;
		bool hasPrevious;
		if (walkedBackward)
		{
			hasNext = fromCursor;
			hasPrevious = extra;
		}
		else
		{
			hasNext = extra;
			hasPrevious = fromCursor;
		}

		if (fetched.Count == 0)
		{
			return new CursorPage<T>([], null, null, false, false, totalCount);
		}

		string? next = null;
		string? previous = null;
		if (hasNext)
		{
			next = CursorCodec.Encode(sortKey, keys[^1], PageDirection.Forward, options);
		}

		if (hasPrevious)
		{
			previous = CursorCodec.Encode(sortKey, keys[0], PageDirection.Backward, options);
		}

		return new CursorPage<T>(fetched, next, previous, hasNext, hasPrevious, totalCount);
	}
}
