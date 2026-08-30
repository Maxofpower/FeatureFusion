namespace BuildingBlocks.Pagination;

/// <summary>
/// Maps a host sort enum to prebuilt <see cref="SortKey{TEntity}"/> instances.
/// Clients never supply property-name strings.
/// </summary>
/// <typeparam name="TEnum">Sort-field enum (e.g. ProductSortField).</typeparam>
/// <typeparam name="TEntity">Row type.</typeparam>
public sealed class SortKeyRegistry<TEnum, TEntity>
	where TEnum : struct, Enum
{
	private readonly Dictionary<TEnum, SortKey<TEntity>> _map = [];

	/// <summary>Registers <paramref name="key"/> for <paramref name="field"/>.</summary>
	/// <param name="field">Enum member.</param>
	/// <param name="key">Prebuilt sort key.</param>
	public SortKeyRegistry<TEnum, TEntity> Add(TEnum field, SortKey<TEntity> key)
	{
		ArgumentNullException.ThrowIfNull(key);
		_map[field] = key;
		return this;
	}

	/// <summary>Returns the key for <paramref name="field"/>, or false if unregistered.</summary>
	/// <param name="field">Enum member.</param>
	/// <param name="key">Key when found.</param>
	public bool TryGet(TEnum field, out SortKey<TEntity> key)
		=> _map.TryGetValue(field, out key!);

	/// <summary>Returns the key or throws.</summary>
	/// <param name="field">Enum member.</param>
	public SortKey<TEntity> Get(TEnum field)
	{
		if (_map.TryGetValue(field, out var key))
		{
			return key;
		}

		throw new InvalidOperationException($"No SortKey is registered for '{field}'.");
	}

	/// <summary>True when every defined enum member has a key.</summary>
	public bool IsComplete()
	{
		foreach (var value in Enum.GetValues<TEnum>())
		{
			if (!_map.ContainsKey(value))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Throws if any enum member is missing.</summary>
	public void EnsureComplete()
	{
		if (!IsComplete())
		{
			var missing = Enum.GetValues<TEnum>().Where(v => !_map.ContainsKey(v));
			throw new InvalidOperationException(
				"SortKeyRegistry is incomplete. Missing: " + string.Join(", ", missing));
		}
	}
}
