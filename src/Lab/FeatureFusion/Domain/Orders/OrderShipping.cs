using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Orders;

/// <summary>Shipping snapshot captured at checkout (not a courier integration).</summary>
public sealed class OrderShipping : ValueObject
{
	public string RecipientName { get; private set; } = "";
	public string Line1 { get; private set; } = "";
	public string City { get; private set; } = "";
	public string PostalCode { get; private set; } = "";
	public string Country { get; private set; } = "";
	public string Method { get; private set; } = "";
	public ShippingStatus Status { get; private set; }

	private OrderShipping()
	{
	}

	public static OrderShipping None() => new()
	{
		Status = ShippingStatus.None,
		Method = "",
		RecipientName = "",
		Line1 = "",
		City = "",
		PostalCode = "",
		Country = ""
	};

	public static OrderShipping Create(
		string recipientName,
		string line1,
		string city,
		string postalCode,
		string country,
		string method,
		ShippingStatus status = ShippingStatus.Pending)
	{
		if (string.IsNullOrWhiteSpace(recipientName))
			throw new DomainException("Shipping recipient is required.");
		if (string.IsNullOrWhiteSpace(line1))
			throw new DomainException("Shipping address line is required.");
		if (string.IsNullOrWhiteSpace(city))
			throw new DomainException("Shipping city is required.");
		if (string.IsNullOrWhiteSpace(postalCode))
			throw new DomainException("Shipping postal code is required.");
		if (string.IsNullOrWhiteSpace(country) || country.Trim().Length != 2)
			throw new DomainException("Shipping country must be a 2-letter code.");
		if (string.IsNullOrWhiteSpace(method))
			throw new DomainException("Shipping method is required.");

		return new OrderShipping
		{
			RecipientName = recipientName.Trim(),
			Line1 = line1.Trim(),
			City = city.Trim(),
			PostalCode = postalCode.Trim(),
			Country = country.Trim().ToUpperInvariant(),
			Method = method.Trim(),
			Status = status
		};
	}

	protected override IEnumerable<object?> GetEqualityComponents()
	{
		yield return RecipientName;
		yield return Line1;
		yield return City;
		yield return PostalCode;
		yield return Country;
		yield return Method;
		yield return Status;
	}
}
