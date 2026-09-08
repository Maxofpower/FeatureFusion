using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;
using FluentValidation;

namespace FeatureFusion.Features.Checkout;

/// <summary>
/// Checkout orchestration intent: tax + shipping + payment, then <c>CreateOrderCommand</c>.
/// Clients never send prices or payment card data.
/// </summary>
[McpTool(
	"orders.checkout",
	Description = "Checkout the customer's cart (tax, shipping, demo payment, create order)",
	Idempotent = true,
	RequireConfirmation = true)]
public sealed class CheckoutCommand : ICommand<Result<CheckoutResponse>>
{
	public int CustomerId { get; set; }
	public string RecipientName { get; set; } = string.Empty;
	public string Line1 { get; set; } = string.Empty;
	public string City { get; set; } = string.Empty;
	public string PostalCode { get; set; } = string.Empty;

	/// <summary>ISO 3166-1 alpha-2 country code.</summary>
	public string Country { get; set; } = string.Empty;
}

/// <summary>FluentValidation for checkout intent — enforced by <c>ValidationBehavior</c>.</summary>
public sealed class CheckoutCommandValidator : AbstractValidator<CheckoutCommand>
{
	public CheckoutCommandValidator()
	{
		RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Customer id is required.");
		RuleFor(x => x.RecipientName).NotEmpty().WithMessage("Recipient name is required.");
		RuleFor(x => x.Line1).NotEmpty().WithMessage("Shipping address line is required.");
		RuleFor(x => x.City).NotEmpty().WithMessage("Shipping city is required.");
		RuleFor(x => x.PostalCode).NotEmpty().WithMessage("Shipping postal code is required.");
		RuleFor(x => x.Country)
			.NotEmpty()
			.Must(c => c.Trim().Length == 2)
			.WithMessage("Country must be a 2-letter code.");
	}
}

/// <summary>Successful checkout projection (order + totals + payment outcome).</summary>
public sealed record CheckoutResponse(
	Guid OrderId,
	int DomainOrderId,
	string OrderNumber,
	string Status,
	decimal Subtotal,
	decimal TaxAmount,
	decimal ShippingAmount,
	decimal GrandTotal,
	string Currency,
	string PaymentDecision,
	DateTime OrderDate);
