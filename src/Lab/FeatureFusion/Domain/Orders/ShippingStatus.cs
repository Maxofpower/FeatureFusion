namespace FeatureFusion.Domain.Orders;

/// <summary>Minimal shipping lifecycle (no carrier integration).</summary>
public enum ShippingStatus
{
	None = 0,
	Pending = 1,
	Scheduled = 2
}
