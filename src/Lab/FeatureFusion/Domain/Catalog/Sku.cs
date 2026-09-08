using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>Stock-keeping unit. Unique per catalog product.</summary>
public sealed class Sku : ValueObject
{
	/// <summary>Normalized SKU text.</summary>
	public string Value { get; }

	private Sku(string value) => Value = value;

	/// <summary>Creates a SKU (1–64 characters).</summary>
	public static Sku Create(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new DomainException("SKU is required.");
		var trimmed = value.Trim();
		if (trimmed.Length > 64)
			throw new DomainException("SKU cannot exceed 64 characters.");
		return new Sku(trimmed);
	}

	/// <inheritdoc />
	protected override IEnumerable<object?> GetEqualityComponents()
	{
		yield return Value.ToUpperInvariant();
	}

	/// <inheritdoc />
	public override string ToString() => Value;

	/// <summary>Implicit to the persistence string.</summary>
	public static implicit operator string(Sku sku) => sku.Value;
}
