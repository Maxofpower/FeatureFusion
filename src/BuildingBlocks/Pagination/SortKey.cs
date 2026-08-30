namespace BuildingBlocks.Pagination;

/// <summary>
/// Compiled keyset: ordered sort slots ending in a unique column.
/// Build with <see cref="SortKey.For{T}"/> and finish with <c>ThenByUnique</c>.
/// </summary>
/// <typeparam name="T">Row type.</typeparam>
public sealed class SortKey<T>
{
	internal SortKey(IReadOnlyList<SortSlot> slots)
	{
		if (slots.Count == 0)
		{
			throw new PaginationException(
				PaginationErrorCode.MissingUniqueKey,
				"SortKey must contain at least one unique column (ThenByUnique).");
		}

		if (!slots[^1].IsUnique)
		{
			throw new PaginationException(
				PaginationErrorCode.MissingUniqueKey,
				"SortKey must end with ThenByUnique so the keyset is deterministic.");
		}

		Slots = slots;
		Fingerprint = SqlIdentifier.Fingerprint(slots);
	}

	internal IReadOnlyList<SortSlot> Slots { get; }

	/// <summary>Stable hash of slot paths, directions, and CLR types. Bound into the cursor.</summary>
	public string Fingerprint { get; }

	internal bool HasShadow => Slots.Any(s => s.Kind == SortSlotKind.Shadow);

	internal void EnsureSqlIdentifiers()
	{
		foreach (var slot in Slots)
		{
			if (string.IsNullOrWhiteSpace(slot.SqlIdentifier))
			{
				throw new PaginationException(
					PaginationErrorCode.MissingSqlIdentifier,
					$"Sort slot '{slot.FingerprintPart}' has no sql: identifier (required for Dapper).");
			}

			SqlIdentifier.EnsureValid(slot.SqlIdentifier);
		}
	}

	internal void EnsureNoShadow()
	{
		if (HasShadow)
		{
			throw new PaginationException(
				PaginationErrorCode.ShadowNotSupported,
				"ByShadow is EF Core only. Map the column with sql: for Dapper.");
		}
	}
}

/// <summary>Entry point for <see cref="SortKey{T}"/>.</summary>
public static class SortKey
{
	/// <summary>Starts a sort-key builder for <typeparamref name="T"/>.</summary>
	/// <typeparam name="T">Row type.</typeparam>
	public static SortKeyBuilder<T> For<T>() => new();
}
