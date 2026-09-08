namespace BuildingBlocks.Domain;

/// <summary>
/// Strongly-typed identity wrapping a primitive <typeparamref name="TId"/>.
/// Derive one type per aggregate or entity (for example <c>OrderId : AggregateId&lt;int&gt;</c>)
/// so ids cannot be mixed at compile time. Convert to the primitive only at persistence and HTTP boundaries.
/// </summary>
/// <typeparam name="TId">Underlying primitive type.</typeparam>
public abstract record Identity<TId> : IIdentity<TId>
	where TId : notnull
{
	/// <summary>Stores the primitive. Derived types may constrain allowed values in their constructors.</summary>
	/// <param name="value">Underlying primitive value.</param>
	protected Identity(TId value)
	{
		ArgumentNullException.ThrowIfNull(value);
		Value = value;
	}

	/// <inheritdoc />
	public TId Value { get; init; }

	/// <summary>Converts to the underlying primitive for queries, EF mappings, and APIs.</summary>
	public static implicit operator TId(Identity<TId> id)
	{
		ArgumentNullException.ThrowIfNull(id);
		return id.Value;
	}

	/// <inheritdoc />
	public override string ToString() => $"{GetType().Name}[{Value}]";
}
