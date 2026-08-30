using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Infrastructure.Internal;

internal static class CursorDbContext
{
	public static object?[] ExtractKeys<T>(T row, SortKey<T> sortKey, DbContext? ctx)
	{
		var values = new object?[sortKey.Slots.Count];
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			var slot = sortKey.Slots[i];
			if (slot.Kind == SortSlotKind.Shadow)
			{
				if (ctx is null)
				{
					throw new PaginationException(
						PaginationErrorCode.UnknownShadowProperty,
						"Shadow properties require an EF Core DbContext to read after materialize.");
				}

				values[i] = ctx.Entry(row!).Property(slot.ShadowName!).CurrentValue;
			}
			else
			{
				values[i] = KeyExtractor.GetValue(slot, row!);
			}
		}

		return values;
	}

	public static void EnsureShadowProperties<T>(IQueryable<T> query, SortKey<T> sortKey)
	{
		if (!sortKey.HasShadow)
		{
			return;
		}

		var ctx = TryGet(query);
		if (ctx is null)
		{
			throw new PaginationException(
				PaginationErrorCode.UnknownShadowProperty,
				"Shadow properties require an EF Core DbContext.");
		}

		var entity = ctx.Model.FindEntityType(typeof(T));
		foreach (var slot in sortKey.Slots)
		{
			if (slot.Kind != SortSlotKind.Shadow)
			{
				continue;
			}

			if (entity is null || entity.FindProperty(slot.ShadowName!) is null)
			{
				throw new PaginationException(
					PaginationErrorCode.UnknownShadowProperty,
					$"Shadow property '{slot.ShadowName}' is not defined on '{typeof(T).Name}'.");
			}
		}
	}

	public static DbContext? TryGet(IQueryable query)
	{
		foreach (var candidate in new object?[] { query, query.Provider })
		{
			if (candidate is not IInfrastructure<IServiceProvider> infra)
			{
				continue;
			}

			var current = infra.GetService<ICurrentDbContext>();
			if (current?.Context is not null)
			{
				return current.Context;
			}
		}

		return null;
	}
}
