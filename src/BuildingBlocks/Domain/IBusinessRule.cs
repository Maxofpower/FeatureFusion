namespace BuildingBlocks.Domain;

/// <summary>Domain invariant checked before applying a state change.</summary>
public interface IBusinessRule
{
	/// <summary>Message returned when <see cref="IsBroken"/> is true.</summary>
	string Message { get; }

	/// <summary>Returns true when the invariant does not hold.</summary>
	bool IsBroken();
}
