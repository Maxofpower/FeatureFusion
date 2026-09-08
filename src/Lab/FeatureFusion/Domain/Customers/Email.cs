using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Customers;

/// <summary>Customer email address (customer bounded context).</summary>
public sealed class Email : ValueObject
{
	/// <summary>Normalized email text.</summary>
	public string Value { get; }

	private Email(string value) => Value = value;

	/// <summary>Creates a validated email.</summary>
	public static Email Create(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || !value.Contains('@', StringComparison.Ordinal))
			throw new DomainException("Email must be a non-empty address.");
		var trimmed = value.Trim();
		if (trimmed.Length > 256)
			throw new DomainException("Email cannot exceed 256 characters.");
		return new Email(trimmed);
	}

	/// <inheritdoc />
	protected override IEnumerable<object?> GetEqualityComponents()
	{
		yield return Value.ToLowerInvariant();
	}

	/// <inheritdoc />
	public override string ToString() => Value;

	/// <summary>Implicit to the persistence string.</summary>
	public static implicit operator string(Email email) => email.Value;
}
