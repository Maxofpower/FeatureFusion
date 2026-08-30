using System.Collections.Concurrent;
using System.Reflection;

namespace BuildingBlocks.Pagination;

internal static class KeyExtractor
{
	private static readonly ConcurrentDictionary<LambdaExpression, Delegate> Cache = new();

	public static object? GetValue(SortSlot slot, object item)
	{
		if (slot.Kind == SortSlotKind.Shadow)
		{
			return GetPropertyOrThrow(item, slot.ShadowName!);
		}

		return Invoke(slot.Accessor!, item);
	}

	public static object?[] GetValues<T>(T item, SortKey<T> sortKey)
	{
		if (sortKey.HasShadow)
		{
			throw new PaginationException(
				PaginationErrorCode.ShadowNotSupported,
				"Extracting shadow keys from a CLR instance is not supported. The EF adapter reads shadow via DbContext.Entry.");
		}

		var values = new object?[sortKey.Slots.Count];
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			values[i] = Invoke(sortKey.Slots[i].Accessor!, item!);
		}

		return values;
	}

	public static object?[] GetValuesFromMap<T>(T item, SortKey<T> sortKey)
	{
		var values = new object?[sortKey.Slots.Count];
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			var slot = sortKey.Slots[i];
			var name = slot.Kind == SortSlotKind.Shadow
				? slot.ShadowName!
				: LeafName(slot.FingerprintPart);
			values[i] = GetPropertyOrThrow(item!, name);
		}

		return values;
	}

	private static object? Invoke(LambdaExpression accessor, object? item)
	{
		var del = Cache.GetOrAdd(accessor, static a => a.Compile());
		return del.DynamicInvoke(item);
	}

	private static string LeafName(string path)
	{
		var span = path.AsSpan();
		var dot = span.LastIndexOf('.');
		return dot < 0 ? path : path[(dot + 1)..];
	}

	private static object? GetPropertyOrThrow(object item, string name)
	{
		var prop = item.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
		if (prop is null)
		{
			throw new PaginationException(
				PaginationErrorCode.MissingKeyColumn,
				$"Projected type '{item.GetType().Name}' is missing keyset member '{name}'.");
		}

		return prop.GetValue(item);
	}
}
