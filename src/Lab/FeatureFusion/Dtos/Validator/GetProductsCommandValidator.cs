
using FeatureFusion.Domain.Entities;
using FeatureFusion.Features.Products.Queries;
using FeatureFusion.Infrastructure.CursorPagination;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Dtos.Validator
{
	public sealed class GetProductsCommandValidator : AbstractValidator<GetProductsQuery>
	{
		public GetProductsCommandValidator()
		{
			RuleFor(x => x.Limit)
				.InclusiveBetween(1, 100)
				.WithMessage("Limit must be between 1 and 100");

			RuleFor(x => x.SortBy)
				.IsInEnum()
				.WithMessage("Invalid sort field");

			RuleFor(x => x.SortDirection)
				.IsInEnum()
				.WithMessage("Invalid sort direction");

			RuleFor(x => x.Cursor)
				.Must(BeValidCursor)
				.When(x => !string.IsNullOrEmpty(x.Cursor))
				.WithMessage("Invalid cursor format")
				.DependentRules(() =>
				{
					RuleFor(x => x)
						.Must(BeCursorConsistentWithSort)
						.WithMessage("Cursor sort field doesn't match requested sort field");
				});
		}

		private static bool BeValidCursor(string cursor)
		{
			if (string.IsNullOrEmpty(cursor)) return true;

			try
			{
				var cursorData = CursorFactory.Decode(cursor);
				return cursorData != null &&
					   !string.IsNullOrEmpty(cursorData.SortBy) &&
					   cursorData.LastValue != null;
			}
			catch
			{
				return false;
			}
		}

		private static bool BeCursorConsistentWithSort(GetProductsQuery command)
		{
			if (string.IsNullOrEmpty(command.Cursor)) return true;

			var cursorData = CursorFactory.Decode(command.Cursor);
			if (cursorData == null) return false;

			var expectedSortBy = command.SortBy switch
			{
				ProductSortField.Id => nameof(Product.Id),
				ProductSortField.Name => nameof(Product.Name),
				ProductSortField.Price => nameof(Product.Price),
				ProductSortField.CreatedAt => nameof(Product.CreatedAt),
				_ => throw new ArgumentOutOfRangeException()
			};

			return cursorData.SortBy.Equals(expectedSortBy, StringComparison.Ordinal);
		}
		public async Task<ValidationResult> ValidateWithResultAsync(GetProductsQuery item)
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

				var problemDetails = new ValidationProblemDetails(validationErrors)
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
