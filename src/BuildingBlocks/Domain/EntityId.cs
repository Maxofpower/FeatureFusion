namespace BuildingBlocks.Domain;

/// <summary>
/// Typed identity intended for child entities inside an aggregate (order lines, images).
/// Hosts specialize: <c>public sealed record OrderItemId : EntityId&lt;int&gt;</c>.
/// </summary>
/// <typeparam name="TId">Underlying primitive type.</typeparam>
public abstract record EntityId<TId> : Identity<TId>
	where TId : notnull
{
	/// <summary>Creates an entity identity.</summary>
	/// <param name="value">Underlying primitive value.</param>
	protected EntityId(TId value) : base(value)
	{
	}
}