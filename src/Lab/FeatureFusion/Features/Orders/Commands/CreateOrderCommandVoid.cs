using FeatureFusion.Dtos;
using BuildingBlocks.Mediator;
using FeatureFusion.Models.Validator;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Orders.Commands
{
	public class CreateOrderCommandVoid : ICommand
	{
		public int ProductId { get; set; }
		public int Quantity { get; set; }
		public int CustomerId { get; set; }
	}

	public class CreateOrderCommandVoidValidator : BaseValidator<CreateOrderCommandVoid>
	{
		private readonly ILogger<CreateOrderCommandVoidValidator> _logger;

		public CreateOrderCommandVoidValidator(ILogger<CreateOrderCommandVoidValidator> logger)
		{
			_logger = logger;
			RuleFor(x => x.Quantity)
				.GreaterThan(0).WithMessage("Quantity must be greater than 0");
		}

		public async Task<ValidationResult> ValidateWithResultAsync(CreateOrderCommandVoid item)
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

				_logger.LogError("validation error on {Command}: {Errors}", nameof(CreateOrderCommandVoid), validationErrors);

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
