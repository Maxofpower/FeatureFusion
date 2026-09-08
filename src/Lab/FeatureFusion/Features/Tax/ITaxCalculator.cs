using FeatureFusion.Domain.Orders;

namespace FeatureFusion.Features.Tax;

public sealed record TaxCalculation(decimal TaxAmount);

/// <summary>Deterministic demo tax — not a tax engine.</summary>
public interface ITaxCalculator
{
	TaxCalculation Calculate(decimal lineSubtotal);
}

public sealed class DemoTaxCalculator : ITaxCalculator
{
	public const decimal Rate = 0.10m;

	public TaxCalculation Calculate(decimal lineSubtotal)
	{
		if (lineSubtotal < 0)
			throw new ArgumentOutOfRangeException(nameof(lineSubtotal));
		var tax = decimal.Round(lineSubtotal * Rate, 2, MidpointRounding.AwayFromZero);
		return new TaxCalculation(tax);
	}
}
