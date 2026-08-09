using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Infrastructure.Extensions;

public static class FluentValidationProblemExtensions
{
	/// <summary>
	/// Maps <see cref="ValidationException"/> to <see cref="ValidationProblemDetails"/> (400).
	/// </summary>
	public static ValidationProblemDetails ToValidationProblemDetails(this ValidationException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);

		var errors = exception.Errors
			.GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? string.Empty : e.PropertyName)
			.ToDictionary(
				g => g.Key,
				g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

		return new ValidationProblemDetails(errors)
		{
			Status = StatusCodes.Status400BadRequest,
			Title = "One or more validation errors occurred."
		};
	}
}
