using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Catalog;

/// <summary>URL-safe catalog identifier used on listing and detail routes.</summary>
public sealed class Slug : ValueObject
{
	/// <summary>Lowercase hyphenated value.</summary>
	public string Value { get; }

	private Slug(string value) => Value = value;

	/// <summary>Creates a slug (1–128 characters, [a-z0-9-]).</summary>
	public static Slug Create(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new DomainException("Slug is required.");
		var trimmed = value.Trim().ToLowerInvariant();
		if (trimmed.Length > 128)
			throw new DomainException("Slug cannot exceed 128 characters.");
		for (var i = 0; i < trimmed.Length; i++)
		{
			var c = trimmed[i];
			if (c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
				throw new DomainException("Slug may contain only letters, digits, and hyphens.");
		}
		return new Slug(trimmed);
	}

	/// <summary>Builds a slug from a display name.</summary>
	public static Slug FromName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new DomainException("Name is required to build a slug.");
		var chars = name.Trim().ToLowerInvariant().Select(c =>
			c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray();
		var raw = new string(chars);
		while (raw.Contains("--", StringComparison.Ordinal))
			raw = raw.Replace("--", "-", StringComparison.Ordinal);
		return Create(raw.Trim('-'));
	}

	/// <inheritdoc />
	protected override IEnumerable<object?> GetEqualityComponents()
	{
		yield return Value;
	}

	/// <inheritdoc />
	public override string ToString() => Value;

	/// <summary>Implicit conversion to the persisted string.</summary>
	public static implicit operator string(Slug slug) => slug.Value;
}
