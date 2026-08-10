namespace BuildingBlocks.Mediator;

/// <summary>
/// Void response type for void <see cref="ICommand"/> (pipeline and <see cref="ICommand"/> inheritance).
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
{
	private static readonly Unit _value = new();

	/// <summary>The single Unit value.</summary>
	public static ref readonly Unit Value => ref _value;

	/// <summary>A completed task of <see cref="Value"/>.</summary>
	public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(_value);

	/// <inheritdoc />
	public int CompareTo(Unit other) => 0;

	/// <inheritdoc />
	int IComparable.CompareTo(object? obj) => 0;

	/// <inheritdoc />
	public override int GetHashCode() => 0;

	/// <inheritdoc />
	public bool Equals(Unit other) => true;

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is Unit;

	/// <summary>Always true; all Unit instances are equal.</summary>
	public static bool operator ==(Unit first, Unit second) => true;

	/// <summary>Always false; all Unit instances are equal.</summary>
	public static bool operator !=(Unit first, Unit second) => false;

	/// <inheritdoc />
	public override string ToString() => "()";
}
