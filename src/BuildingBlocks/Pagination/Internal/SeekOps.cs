namespace BuildingBlocks.Pagination;

internal static class SeekOps
{
	/// <summary>
	/// True when the slot comparison for this walk uses greater-than (else less-than).
	/// ASC+Forward and DESC+Backward use greater-than.
	/// </summary>
	public static bool UseGreater(SortSlot slot, bool walkBackward)
		=> (slot.Direction == SortDirection.Ascending) ^ walkBackward;

	public static SortDirection OrderDirection(SortSlot slot, bool walkBackward)
		=> walkBackward ? Invert(slot.Direction) : slot.Direction;

	public static SortDirection Invert(SortDirection direction)
		=> direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;

	public static bool TupleEligible<T>(SortKey<T> key, bool walkBackward)
	{
		var first = UseGreater(key.Slots[0], walkBackward);
		for (var i = 1; i < key.Slots.Count; i++)
		{
			if (UseGreater(key.Slots[i], walkBackward) != first)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// True when every slot is a non-nullable value type (no <c>string</c> / reference nullables).
	/// Used by EF Npgsql row comparison for any slot count. Postgres tuple comparison with SQL NULL
	/// is not equivalent to <see cref="NullOrder"/> seek logic.
	/// </summary>
	public static bool TupleSlotsNonNull<T>(SortKey<T> key)
	{
		for (var i = 0; i < key.Slots.Count; i++)
		{
			var type = key.Slots[i].DeclaredType;
			if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null)
			{
				return false;
			}
		}

		return true;
	}
}
