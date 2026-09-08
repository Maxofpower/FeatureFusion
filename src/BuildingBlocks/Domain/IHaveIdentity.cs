namespace BuildingBlocks.Domain;

/// <summary>Marks a type that is identified by <see cref="Id"/>.</summary>
public interface IHaveIdentity
{
	/// <summary>Untyped identity (useful when walking mixed aggregates).</summary>
	object Id { get; }
}

/// <summary>Typed identity accessor for <typeparamref name="TId"/>.</summary>
public interface IHaveIdentity<out TId> : IHaveIdentity
	where TId : notnull
{
	/// <summary>Typed primary identity.</summary>
	new TId Id { get; }

	/// <inheritdoc />
	object IHaveIdentity.Id => Id;
}
