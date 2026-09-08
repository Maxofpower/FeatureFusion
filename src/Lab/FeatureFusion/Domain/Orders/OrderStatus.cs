namespace FeatureFusion.Domain.Orders;

/// <summary>Minimal order lifecycle for Demo Commerce.</summary>
public enum OrderStatus
{
	/// <summary>Awaiting confirmation (seed / reserved flows).</summary>
	Pending = 0,

	/// <summary>Accepted (CreateOrder and successful checkout).</summary>
	Placed = 1,

	/// <summary>Cancelled after placement or while pending.</summary>
	Cancelled = 2,

	/// <summary>Payment declined after a pending order was recorded (domain completeness).</summary>
	PaymentFailed = 3
}
