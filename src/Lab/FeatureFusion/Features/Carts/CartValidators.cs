using FluentValidation;

namespace FeatureFusion.Features.Carts;

public sealed class GetCartQueryValidator : AbstractValidator<GetCartQuery>
{
	public GetCartQueryValidator() =>
		RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Customer id is required.");
}

public sealed class AddCartItemCommandValidator : AbstractValidator<AddCartItemCommand>
{
	public AddCartItemCommandValidator()
	{
		RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Customer id is required.");
		RuleFor(x => x.ProductId).GreaterThan(0).WithMessage("Product id is required.");
		RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than 0.");
	}
}

public sealed class UpdateCartItemQuantityCommandValidator : AbstractValidator<UpdateCartItemQuantityCommand>
{
	public UpdateCartItemQuantityCommandValidator()
	{
		RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Customer id is required.");
		RuleFor(x => x.ProductId).GreaterThan(0).WithMessage("Product id is required.");
	}
}

public sealed class RemoveCartItemCommandValidator : AbstractValidator<RemoveCartItemCommand>
{
	public RemoveCartItemCommandValidator()
	{
		RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Customer id is required.");
		RuleFor(x => x.ProductId).GreaterThan(0).WithMessage("Product id is required.");
	}
}

public sealed class ClearCartCommandValidator : AbstractValidator<ClearCartCommand>
{
	public ClearCartCommandValidator() =>
		RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Customer id is required.");
}
