namespace BuildingBlocks.Domain;

/// <summary>
/// Typed identity intended for aggregate roots.
/// Hosts specialize: <c>public sealed record OrderId : AggregateId&lt;int&gt;</c>.
/// </summary>
/// <typeparam name="TId">Underlying primitive type.</typeparam>
public abstract record AggregateId<TId> : Identity<TId>
	where TId : notnull
{
	/// <summary>Creates an aggregate identity.</summary>
	/// <param name="value">Underlying primitive value.</param>
	protected AggregateId(TId value) : base(value)
	{
	}
}