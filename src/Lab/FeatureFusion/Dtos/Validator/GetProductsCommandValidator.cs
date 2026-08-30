using BuildingBlocks.Pagination;
using FeatureFusion.Features.Products.Queries;
using FeatureFusion.Infrastructure.Pagination;
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

			RuleFor(x => x.PageDirection)
				.IsInEnum()
				.WithMessage("Invalid page direction");

			RuleFor(x => x.Cursor)
				.Must(cursor => CursorCodec.TryValidateFormat(cursor))
				.When(x => !string.IsNullOrEmpty(x.Cursor))
				.WithMessage("Invalid cursor format")
				.DependentRules(() =>
				{
					RuleFor(x => x)
						.Must(BeCursorConsistentWithSort)
						.WithMessage("Cursor sort field doesn't match requested sort field");
				});
		}

		private static bool BeCursorConsistentWithSort(GetProductsQuery command)
		{
			if (string.IsNullOrEmpty(command.Cursor)) return true;

			try
			{
				CursorCodec.Validate(
					command.Cursor,
					ProductSortKeys.Resolve(command.SortBy, command.SortDirection));
				return true;
			}
			catch (PaginationException ex) when (ex.Code == PaginationErrorCode.CursorSortMismatch)
			{
				return false;
			}
			catch (PaginationException)
			{
				return false;
			}
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
