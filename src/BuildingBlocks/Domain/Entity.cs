namespace BuildingBlocks.Domain;

/// <summary>
/// Entity with identity-based equality. Prefer a typed <see cref="Identity{TId}"/>
/// (or <see cref="AggregateId{TId}"/> / <see cref="EntityId{TId}"/>) as <typeparamref name="TId"/>.
/// <see cref="Id"/> is protected-set so callers create instances through factories or domain methods.
/// </summary>
/// <typeparam name="TId">Identity type (typed id or primitive).</typeparam>
public abstract class Entity<TId> : IEntity<TId>, IEquatable<Entity<TId>>
	where TId : notnull
{
	/// <inheritdoc />
	public TId Id { get; protected set; } = default!;

	/// <summary>For ORMs. Domain code should use factories.</summary>
	protected Entity()
	{
	}

	/// <summary>When the identity is known at construction.</summary>
	/// <param name="id">Entity identity.</param>
	protected Entity(TId id) => Id = id;

	/// <inheritdoc />
	public void CheckRule(IBusinessRule rule) => Validate(rule);

	/// <summary>Evaluates a rule and throws when broken. Safe for static factories.</summary>
	/// <param name="rule">Rule to evaluate.</param>
	/// <exception cref="BusinessRuleValidationException">Thrown when the rule is broken.</exception>
	public static void Validate(IBusinessRule rule)
	{
		ArgumentNullException.ThrowIfNull(rule);
		if (rule.IsBroken())
			throw new BusinessRuleValidationException(rule);
	}

	/// <inheritdoc />
	public bool Equals(Entity<TId>? other)
	{
		if (other is null)
			return false;
		if (ReferenceEquals(this, other))
			return true;
		if (GetType() != other.GetType())
			return false;
		if (Id is null || other.Id is null)
			return false;
		return EqualityComparer<TId>.Default.Equals(Id, other.Id);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

	/// <inheritdoc />
	public override int GetHashCode() => HashCode.Combine(GetType(), Id);

	/// <summary>Identity equality.</summary>
	public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

	/// <summary>Identity inequality.</summary>
	public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
