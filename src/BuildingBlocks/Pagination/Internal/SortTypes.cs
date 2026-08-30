namespace BuildingBlocks.Pagination;

/// <summary>
/// CLR types that can appear in a keyset slot. Value objects and complex types are not slots;
/// sort a mapped scalar such as Money.Amount, not the object.
/// </summary>
internal static class SortTypes
{
	public static void EnsureSupported(Type declared)
	{
		if (Nullable.GetUnderlyingType(declared) is not null)
		{
			throw new PaginationException(
				PaginationErrorCode.NullableSortUnsupported,
				$"Nullable type '{declared.Name}' cannot be a keyset column. Coalesce in the model (or a computed column) so the slot is non-null. ORDER BY null placement differs by provider and would skip or duplicate rows.");
		}

		if (IsSupported(declared))
		{
			return;
		}

		throw new PaginationException(
			PaginationErrorCode.UnsupportedSortType,
			$"Type '{declared.Name}' cannot be a keyset column. Sort a primitive, string, Guid, DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan, decimal, bool, or enum. For value objects use a nested scalar (for example p.Money.Amount).");
	}

	public static bool IsSupported(Type type)
	{
		if (Nullable.GetUnderlyingType(type) is not null)
		{
			return false;
		}
		if (type.IsEnum)
		{
			return true;
		}

		if (type.IsPrimitive)
		{
			return type != typeof(char) && type != typeof(nint) && type != typeof(nuint);
		}

		return type == typeof(decimal)
			|| type == typeof(string)
			|| type == typeof(DateTime)
			|| type == typeof(DateTimeOffset)
			|| type == typeof(TimeSpan)
			|| type == typeof(Guid)
			|| type == typeof(DateOnly)
			|| type == typeof(TimeOnly);
	}
}
