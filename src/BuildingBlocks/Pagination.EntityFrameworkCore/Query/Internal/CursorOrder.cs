using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Pagination;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Query.Internal;

internal static class CursorOrder
{
	private static readonly MethodInfo OrderBy = Get("OrderBy");
	private static readonly MethodInfo OrderByDescending = Get("OrderByDescending");
	private static readonly MethodInfo ThenBy = Get("ThenBy");
	private static readonly MethodInfo ThenByDescending = Get("ThenByDescending");
	private static readonly ConcurrentDictionary<OrderCacheKey, object> Cache = new();

	public static IQueryable<T> Apply<T>(IQueryable<T> query, SortKey<T> sortKey, bool walkBackward)
	{
		var key = new OrderCacheKey(typeof(T), sortKey.Fingerprint, walkBackward);
		var applicator = (Func<IQueryable<T>, IQueryable<T>>)Cache.GetOrAdd(
			key,
			_ => Build(sortKey, walkBackward));
		return applicator(query);
	}

	private static Func<IQueryable<T>, IQueryable<T>> Build<T>(SortKey<T> sortKey, bool walkBackward)
	{
		var steps = new (MethodInfo Method, LambdaExpression Lambda)[sortKey.Slots.Count];
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			var slot = sortKey.Slots[i];
			var lambda = CursorSlot.Lambda<T>(slot);
			var desc = SeekOps.OrderDirection(slot, walkBackward) == SortDirection.Descending;
			MethodInfo method;
			if (i == 0)
			{
				method = desc ? OrderByDescending : OrderBy;
			}
			else
			{
				method = desc ? ThenByDescending : ThenBy;
			}

			steps[i] = (method.MakeGenericMethod(typeof(T), slot.DeclaredType), lambda);
		}

		return query =>
		{
			IQueryable result = query;
			foreach (var step in steps)
			{
				result = (IQueryable)step.Method.Invoke(null, [result, step.Lambda])!;
			}

			return (IQueryable<T>)result;
		};
	}

	private static MethodInfo Get(string name)
		=> typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Single(m => m.Name == name && m.GetParameters().Length == 2);

	private readonly record struct OrderCacheKey(Type Entity, string Fingerprint, bool WalkBackward);
}
