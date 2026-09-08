using System.Text.Json.Serialization;
using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;
using FeatureFusion.Dtos;
using FeatureFusion.Domain.Orders;
using FeatureFusion.Models.Validator;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Orders.Commands;

/// <summary>
/// Create-order intent. Experiments 1–20 use flat <c>productId</c>/<c>quantity</c>/<c>customerId</c>.
/// Optional <see cref="Items"/> supports multi-line Demo Commerce creates; when omitted, the flat fields form a single line.
/// Clients never send unit price, total, order number, or status.
/// Checkout may set ignored tax/shipping fields via Mediator only.
/// </summary>
[McpTool("orders.create", Description = "Create an order from catalog products", Idempotent = true, RequireConfirmation = true)]
public class CreateOrderCommand : ICommand<Result<OrderResponse>>
{
	public int ProductId { get; set; }
	public int Quantity { get; set; }
	public int CustomerId { get; set; }

	/// <summary>Optional multi-line payload. When present and non-empty, replaces the flat product/quantity line.</summary>
	public List<CreateOrderLineDto>? Items { get; set; }

	/// <summary>Set only by CheckoutCommandHandler (not HTTP/MCP JSON).</summary>
	[JsonIgnore]
	public decimal TaxAmount { get; set; }

	/// <summary>Set only by CheckoutCommandHandler (not HTTP/MCP JSON).</summary>
	[JsonIgnore]
	public decimal ShippingAmount { get; set; }

	/// <summary>Set only by CheckoutCommandHandler (not HTTP/MCP JSON).</summary>
	[JsonIgnore]
	public OrderShipping? Shipping { get; set; }

	/// <summary>Normalized order lines for hashing, validation, and persistence.</summary>
	public IReadOnlyList<CreateOrderLineDto> ResolveLines()
	{
		if (Items is { Count: > 0 })
			return Items;

		return [new CreateOrderLineDto { ProductId = ProductId, Quantity = Quantity }];
	}
}
/// <summary>One requested line (intent only — no client price).</summary>
public sealed class CreateOrderLineDto
{
	public int ProductId { get; set; }
	public int Quantity { get; set; }
}

public class OrderRequestValidator : BaseValidator<CreateOrderCommand>
{
	private readonly ILogger<OrderRequestValidator> _logger;

	public OrderRequestValidator(ILogger<OrderRequestValidator> logger)
	{
		_logger = logger;

		RuleFor(x => x.CustomerId)
			.GreaterThan(0).WithMessage("CustomerId must be greater than 0");

		RuleFor(x => x.TaxAmount)
			.GreaterThanOrEqualTo(0).WithMessage("Tax cannot be negative.");

		RuleFor(x => x.ShippingAmount)
			.GreaterThanOrEqualTo(0).WithMessage("Shipping cannot be negative.");

		RuleFor(x => x)
			.Custom((cmd, ctx) =>
			{
				var lines = cmd.ResolveLines();
				if (lines.Count == 0)
				{
					ctx.AddFailure("Items", "At least one order item is required");
					return;
				}

				for (var i = 0; i < lines.Count; i++)
				{
					if (lines[i].ProductId <= 0)
						ctx.AddFailure($"Items[{i}].ProductId", "ProductId must be greater than 0");
					if (lines[i].Quantity <= 0)
						ctx.AddFailure($"Items[{i}].Quantity", "Quantity must be greater than 0");
				}
			});
	}

	public async Task<ValidationResult> ValidateWithResultAsync(CreateOrderCommand item)
	{
		var validationResult = await ValidateAsync(item);

		if (!validationResult.IsValid)
		{
			var validationErrors = validationResult.Errors
				.GroupBy(e => e.PropertyName)
				.ToDictionary(
					group => group.Key,
					group => group.Select(e => e.ErrorMessage).ToArray());

			_logger.LogError("validation error on {Command}: {Errors}", nameof(CreateOrderCommand), validationErrors);

			var problemDetails = new ValidationProblemDetails
			{
				Status = StatusCodes.Status400BadRequest,
				Title = "One or more validation errors occurred.",
				Errors = validationErrors
			};

			return ValidationResult.Failure(problemDetails);
		}

		return ValidationResult.Success();
	}
}
