namespace FeatureFusion.Features.Payments;

public enum PaymentDecision
{
	Approved = 1,
	Declined = 2
}

public sealed record PaymentChargeRequest(decimal Amount, string Currency, int CustomerId);

public sealed record PaymentChargeResult(PaymentDecision Decision, string Reason);

/// <summary>Demo payment boundary — not a real PSP.</summary>
public interface IPaymentProcessor
{
	Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Deterministic demo processor: declines when the amount's cent fraction equals 13 (e.g. 10.13, 1199.13).
/// </summary>
public sealed class DemoPaymentProcessor : IPaymentProcessor
{
	public Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken)
	{
		var rounded = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
		var cents = (int)(rounded * 100m) % 100;
		if (cents == 13)
		{
			return Task.FromResult(new PaymentChargeResult(
				PaymentDecision.Declined,
				"DemoPaymentProcessor declined amounts ending in .13."));
		}

		return Task.FromResult(new PaymentChargeResult(
			PaymentDecision.Approved,
			"DemoPaymentProcessor approved."));
	}
}
