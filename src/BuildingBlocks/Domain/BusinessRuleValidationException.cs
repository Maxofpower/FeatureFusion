namespace BuildingBlocks.Domain;

/// <summary>Thrown when an <see cref="IBusinessRule"/> is broken.</summary>
public sealed class BusinessRuleValidationException : DomainException
{
	/// <summary>The rule that failed.</summary>
	public IBusinessRule BrokenRule { get; }

	/// <summary>Creates the exception from a broken rule.</summary>
	/// <param name="brokenRule">Rule whose <see cref="IBusinessRule.IsBroken"/> returned true.</param>
	public BusinessRuleValidationException(IBusinessRule brokenRule)
		: base(brokenRule?.Message ?? throw new ArgumentNullException(nameof(brokenRule)))
	{
		BrokenRule = brokenRule;
	}
}
