using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildingBlocks.Pagination.EntityFrameworkCore;

/// <summary>
/// Composite indexes that match a prebuilt <see cref="SortKey{TEntity}"/> (column order and direction).
/// </summary>
public static class KeysetIndexExtensions
{
	/// <summary>
	/// Optional helper: create a composite index that matches <paramref name="sortKey"/>
	/// (same columns, same ASC/DESC). Cursor pagination is correct without an index; on a large table
	/// a matching index is what keeps <c>WHERE (price, id) &gt; … ORDER BY price, id</c> from sorting the table.
	/// Nested paths such as <c>Vendor.Name</c> are not mapped — index that column on the table that owns it.
	/// </summary>
	/// <remarks>
	/// Price-ASC+Id and Price-DESC+Id are two different indexes. Calling <c>HasIndex(Price, Id)</c> twice
	/// is collapsed by EF Core into one, so this helper names each direction mix distinctly.
	/// </remarks>
	/// <typeparam name="TEntity">Entity type.</typeparam>
	/// <param name="builder">Entity builder.</param>
	/// <param name="sortKey">Prebuilt sort key (same instance the query uses).</param>
	public static IndexBuilder HasKeysetIndex<TEntity>(
		this EntityTypeBuilder<TEntity> builder,
		SortKey<TEntity> sortKey)
		where TEntity : class
		=> CreateKeysetIndex(builder, sortKey, nulls: null);

	/// <summary>
	/// Same as <see cref="HasKeysetIndex{TEntity}(EntityTypeBuilder{TEntity}, SortKey{TEntity})"/>,
	/// and when the Npgsql provider is present, calls <c>HasNullSortOrder</c> so index NULLS
	/// placement matches <paramref name="nulls"/>. Pass this overload only when you want that
	/// metadata; the one-argument form does not change existing index null-sort annotations.
	/// Npgsql omits NULLS from DDL when they match the column's ASC/DESC default.
	/// </summary>
	/// <typeparam name="TEntity">Entity type.</typeparam>
	/// <param name="builder">Entity builder.</param>
	/// <param name="sortKey">Prebuilt sort key.</param>
	/// <param name="nulls">Null placement to store on the Npgsql index metadata.</param>
	public static IndexBuilder HasKeysetIndex<TEntity>(
		this EntityTypeBuilder<TEntity> builder,
		SortKey<TEntity> sortKey,
		NullOrder nulls)
		where TEntity : class
		=> CreateKeysetIndex(builder, sortKey, nulls);

	private static IndexBuilder CreateKeysetIndex<TEntity>(
		EntityTypeBuilder<TEntity> builder,
		SortKey<TEntity> sortKey,
		NullOrder? nulls)
		where TEntity : class
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(sortKey);

		var names = new string[sortKey.Slots.Count];
		var descending = new bool[sortKey.Slots.Count];
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			var slot = sortKey.Slots[i];
			names[i] = ColumnName(slot);
			descending[i] = slot.Direction == SortDirection.Descending;
		}

		// EF treats HasIndex(same columns) as one index; a distinct name is required for ASC vs DESC.
		var modelName = "IX_" + typeof(TEntity).Name + "_" + string.Join("_", names) + "_"
			+ string.Concat(descending.Select(d => d ? "D" : "A"));
		var index = builder.HasIndex(names, modelName);
		if (descending.Any(static d => d))
		{
			index.IsDescending(descending);
		}

		if (nulls is { } order)
		{
			NpgsqlIndexNullSort.TryApply(index, sortKey.Slots.Count, order);
		}

		return index;
	}

	private static string ColumnName(SortSlot slot)
	{
		if (slot.Kind == SortSlotKind.Shadow)
		{
			return slot.ShadowName!;
		}

		var path = slot.FingerprintPart;
		if (path.Contains('.', StringComparison.Ordinal))
		{
			throw new PaginationException(
				PaginationErrorCode.UnsupportedKeysetIndex,
				$"HasKeysetIndex cannot map nested path '{path}'. Index the store columns yourself.");
		}

		return path;
	}
}
