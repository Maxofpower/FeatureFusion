namespace BuildingBlocks.Pagination;

/// <summary>
/// Fluent sort-key builder. Finish with <see cref="ThenByUnique{TValue}"/> so the keyset is deterministic.
/// </summary>
/// <typeparam name="T">Row type.</typeparam>
public sealed class SortKeyBuilder<T>
{
	private readonly List<SortSlot> _slots = [];

	/// <summary>Adds an ascending mapped property.</summary>
	/// <typeparam name="TValue">Column CLR type.</typeparam>
	/// <param name="accessor">Property chain, e.g. <c>p =&gt; p.Price</c>.</param>
	/// <param name="sql">SQL identifier for Dapper (optional for EF).</param>
	public SortKeyBuilder<T> By<TValue>(Expression<Func<T, TValue>> accessor, string? sql = null)
		=> AddExpression(accessor, SortDirection.Ascending, unique: false, sql);

	/// <summary>Adds a descending mapped property.</summary>
	/// <typeparam name="TValue">Column CLR type.</typeparam>
	/// <param name="accessor">Property chain.</param>
	/// <param name="sql">SQL identifier for Dapper.</param>
	public SortKeyBuilder<T> ByDescending<TValue>(Expression<Func<T, TValue>> accessor, string? sql = null)
		=> AddExpression(accessor, SortDirection.Descending, unique: false, sql);

	/// <summary>Adds an ascending mapped property (alias of <see cref="By{TValue}"/> after the first column).</summary>
	/// <typeparam name="TValue">Column CLR type.</typeparam>
	/// <param name="accessor">Property chain.</param>
	/// <param name="sql">SQL identifier for Dapper.</param>
	public SortKeyBuilder<T> ThenBy<TValue>(Expression<Func<T, TValue>> accessor, string? sql = null)
		=> By(accessor, sql);

	/// <summary>Adds a descending mapped property after the first column.</summary>
	/// <typeparam name="TValue">Column CLR type.</typeparam>
	/// <param name="accessor">Property chain.</param>
	/// <param name="sql">SQL identifier for Dapper.</param>
	public SortKeyBuilder<T> ThenByDescending<TValue>(Expression<Func<T, TValue>> accessor, string? sql = null)
		=> ByDescending(accessor, sql);

	/// <summary>
	/// EF Core shadow / store-only column (compiles to <c>EF.Property&lt;TValue&gt;</c>).
	/// Dapper rejects keys that include this slot.
	/// </summary>
	/// <typeparam name="TValue">Column CLR type.</typeparam>
	/// <param name="name">Shadow property name.</param>
	/// <param name="sql">Unused for EF; Dapper still rejects shadow slots.</param>
	public SortKeyBuilder<T> ByShadow<TValue>(string name, string? sql = null)
		=> AddShadow<TValue>(name, SortDirection.Ascending, unique: false, sql);

	/// <summary>Descending shadow column.</summary>
	/// <typeparam name="TValue">Column CLR type.</typeparam>
	/// <param name="name">Shadow property name.</param>
	/// <param name="sql">SQL identifier (unused; Dapper rejects shadow).</param>
	public SortKeyBuilder<T> ByShadowDescending<TValue>(string name, string? sql = null)
		=> AddShadow<TValue>(name, SortDirection.Descending, unique: false, sql);

	/// <summary>Appends the unique tiebreaker (required) and returns the compiled key.</summary>
	/// <typeparam name="TValue">Unique column CLR type (int, long, Guid, string, …). Sort the stored scalar, not a value-object wrapper.</typeparam>
	/// <param name="accessor">Unique property.</param>
	/// <param name="sql">SQL identifier for Dapper.</param>
	public SortKey<T> ThenByUnique<TValue>(Expression<Func<T, TValue>> accessor, string? sql = null)
	{
		AddExpression(accessor, SortDirection.Ascending, unique: true, sql);
		return new SortKey<T>(_slots.ToArray());
	}

	/// <summary>Descending unique tiebreaker.</summary>
	/// <typeparam name="TValue">Unique column CLR type.</typeparam>
	/// <param name="accessor">Unique property.</param>
	/// <param name="sql">SQL identifier for Dapper.</param>
	public SortKey<T> ThenByUniqueDescending<TValue>(Expression<Func<T, TValue>> accessor, string? sql = null)
	{
		AddExpression(accessor, SortDirection.Descending, unique: true, sql);
		return new SortKey<T>(_slots.ToArray());
	}

	/// <summary>Unique shadow tiebreaker (EF only).</summary>
	/// <typeparam name="TValue">Column CLR type.</typeparam>
	/// <param name="name">Shadow property name.</param>
	/// <param name="sql">Unused for Dapper (rejected).</param>
	public SortKey<T> ThenByUniqueShadow<TValue>(string name, string? sql = null)
	{
		AddShadow<TValue>(name, SortDirection.Ascending, unique: true, sql);
		return new SortKey<T>(_slots.ToArray());
	}

	/// <summary>Descending unique shadow tiebreaker (EF only).</summary>
	/// <typeparam name="TValue">Column CLR type.</typeparam>
	/// <param name="name">Shadow property name.</param>
	/// <param name="sql">Unused for Dapper (rejected).</param>
	public SortKey<T> ThenByUniqueShadowDescending<TValue>(string name, string? sql = null)
	{
		AddShadow<TValue>(name, SortDirection.Descending, unique: true, sql);
		return new SortKey<T>(_slots.ToArray());
	}

	private SortKeyBuilder<T> AddExpression<TValue>(
		Expression<Func<T, TValue>> accessor,
		SortDirection direction,
		bool unique,
		string? sql)
	{
		ArgumentNullException.ThrowIfNull(accessor);
		if (sql is not null)
		{
			SqlIdentifier.EnsureValid(sql);
		}

		SortTypes.EnsureSupported(typeof(TValue));

		_slots.Add(new SortSlot
		{
			Kind = SortSlotKind.Expression,
			Direction = direction,
			ClrType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue),
			DeclaredType = typeof(TValue),
			FingerprintPart = ExpressionPath.Get(accessor),
			IsUnique = unique,
			SqlIdentifier = sql,
			Accessor = ExpressionPath.Box(accessor)
		});
		return this;
	}

	private SortKeyBuilder<T> AddShadow<TValue>(string name, SortDirection direction, bool unique, string? sql)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new PaginationException(
				PaginationErrorCode.UnknownShadowProperty,
				"Shadow property name is required.");
		}

		if (sql is not null)
		{
			SqlIdentifier.EnsureValid(sql);
		}

		SortTypes.EnsureSupported(typeof(TValue));

		_slots.Add(new SortSlot
		{
			Kind = SortSlotKind.Shadow,
			Direction = direction,
			ClrType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue),
			DeclaredType = typeof(TValue),
			FingerprintPart = "shadow:" + name,
			IsUnique = unique,
			SqlIdentifier = sql,
			ShadowName = name
		});
		return this;
	}
}
