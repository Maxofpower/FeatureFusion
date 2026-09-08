using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Orders;

/// <summary>Public order number (business key, not the persistence id).</summary>
public sealed class OrderNumber : ValueObject
{
	/// <summary>Normalized order number.</summary>
	public string Value { get; }

	private OrderNumber(string value) => Value = value;

	/// <summary>Creates an order number (1–32 characters).</summary>
	public static OrderNumber Create(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new DomainException("Order number is required.");
		var trimmed = value.Trim();
		if (trimmed.Length > 32)
			throw new DomainException("Order number cannot exceed 32 characters.");
		return new OrderNumber(trimmed);
	}

	/// <inheritdoc />
	protected override IEnumerable<object?> GetEqualityComponents()
	{
		yield return Value.ToUpperInvariant();
	}

	/// <inheritdoc />
	public override string ToString() => Value;

	/// <summary>Implicit to the persistence string.</summary>
	public static implicit operator string(OrderNumber number) => number.Value;
}
