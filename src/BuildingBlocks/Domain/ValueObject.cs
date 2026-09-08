namespace BuildingBlocks.Domain;

/// <summary>
/// Immutable value compared by structure (<see cref="GetEqualityComponents"/>), not by reference.
/// Prefer a static <c>Create</c> (or similar) factory that validates and returns the VO.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
	/// <summary>Ordered components used for equality and hashing.</summary>
	protected abstract IEnumerable<object?> GetEqualityComponents();

	/// <inheritdoc />
	public bool Equals(ValueObject? other)
	{
		if (other is null || GetType() != other.GetType())
			return false;

		return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
	}

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

	/// <inheritdoc />
	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var component in GetEqualityComponents())
			hash.Add(component);
		return hash.ToHashCode();
	}

	/// <summary>Structural equality (nulls are equal to each other).</summary>
	public static bool operator ==(ValueObject? left, ValueObject? right)
	{
		if (left is null && right is null)
			return true;
		if (left is null || right is null)
			return false;
		return left.Equals(right);
	}

	/// <summary>Structural inequality.</summary>
	public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
