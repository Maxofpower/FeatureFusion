using FeatureFusion.Domain.Orders;

namespace FeatureFusion.Features.Shipping;

public sealed record ShippingQuote(decimal Fee, OrderShipping Details);

/// <summary>Deterministic flat-rate demo shipping.</summary>
public interface IShippingPolicy
{
	ShippingQuote Quote(string recipientName, string line1, string city, string postalCode, string country);
}

public sealed class DemoShippingPolicy : IShippingPolicy
{
	public const decimal FlatFee = 4.99m;
	public const string MethodName = "Demo Standard";

	public ShippingQuote Quote(string recipientName, string line1, string city, string postalCode, string country)
	{
		var details = OrderShipping.Create(
			recipientName,
			line1,
			city,
			postalCode,
			country,
			MethodName,
			ShippingStatus.Pending);
		return new ShippingQuote(FlatFee, details);
	}
}
