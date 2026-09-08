namespace BuildingBlocks.Domain;

/// <summary>
/// Named enumeration class: comparable, equality by <see cref="Id"/>.
/// Prefer this over a raw enum when the value carries behavior; otherwise a C# enum is fine.
/// </summary>
public abstract class Enumeration : IEquatable<Enumeration>, IComparable<Enumeration>
{
	/// <summary>Creates an enumeration member.</summary>
	/// <param name="id">Stable numeric identifier (persisted).</param>
	/// <param name="name">Display name.</param>
	protected Enumeration(int id, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Enumeration name is required.", nameof(name));
		Id = id;
		Name = name;
	}

	/// <summary>Numeric identity.</summary>
	public int Id { get; }

	/// <summary>Human-readable name.</summary>
	public string Name { get; }

	/// <summary>All public static members of <typeparamref name="T"/> declared on that type.</summary>
	public static IReadOnlyList<T> GetAll<T>()
		where T : Enumeration
	{
		return typeof(T)
			.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
			.Select(f => f.GetValue(null))
			.OfType<T>()
			.ToList();
	}

	/// <summary>Resolves a member by numeric id.</summary>
	public static T FromId<T>(int id)
		where T : Enumeration
	{
		return GetAll<T>().SingleOrDefault(x => x.Id == id)
			?? throw new DomainException($"Unknown {typeof(T).Name} id '{id}'.");
	}

	/// <summary>Resolves a member by name (ordinal ignore-case).</summary>
	public static T FromName<T>(string name)
		where T : Enumeration
	{
		return GetAll<T>().SingleOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
			?? throw new DomainException($"Unknown {typeof(T).Name} name '{name}'.");
	}

	/// <inheritdoc />
	public bool Equals(Enumeration? other)
	{
		if (other is null)
			return false;
		if (ReferenceEquals(this, other))
			return true;
		return GetType() == other.GetType() && Id == other.Id;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is Enumeration other && Equals(other);

	/// <inheritdoc />
	public override int GetHashCode() => HashCode.Combine(GetType(), Id);

	/// <inheritdoc />
	public override string ToString() => Name;

	/// <inheritdoc />
	public int CompareTo(Enumeration? other) => Id.CompareTo(other?.Id ?? 0);

	/// <summary>Value equality.</summary>
	public static bool operator ==(Enumeration? left, Enumeration? right) => Equals(left, right);

	/// <summary>Value inequality.</summary>
	public static bool operator !=(Enumeration? left, Enumeration? right) => !Equals(left, right);
}
