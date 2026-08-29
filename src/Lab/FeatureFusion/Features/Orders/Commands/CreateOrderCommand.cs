using FeatureFusion.Dtos;
using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;
using FeatureFusion.Models.Validator;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using static FeatureFusion.Features.Orders.Commands.CreateOrderCommandHandler;


namespace FeatureFusion.Features.Orders.Commands
{
	[McpTool("orders.create", Description = "Create an order", Idempotent = true, RequireConfirmation = true)]
	public class CreateOrderCommand : ICommand<Result<OrderResponse>>
	{
		public int ProductId { get; set; }
		public int Quantity { get; set; }
		public int CustomerId { get; set; }
	}
	public class OrderRequestValidator : BaseValidator<CreateOrderCommand>
	{
		private readonly ILogger<OrderRequestValidator> _logger;

		public OrderRequestValidator(ILogger<OrderRequestValidator> logger)
		{
			_logger = logger;
			RuleFor(x => x.Quantity)
		   .GreaterThan(0).WithMessage("Quantity must be greater than 0");
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
						group => group.Select(e => e.ErrorMessage).ToArray()
					);

				_logger.LogError($"validation error on {nameof(GreetingDto)}: {validationErrors}");

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

}
