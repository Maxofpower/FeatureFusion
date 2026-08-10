using FeatureFusion.Infrastructure.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace FeatureFusion.Infrastructure.Exceptions;

/// <summary>
/// Maps FluentValidation <see cref="ValidationException"/> to HTTP 400 + ValidationProblemDetails.
/// </summary>
public sealed class ValidationExceptionHandler : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(
		HttpContext httpContext,
		Exception exception,
		CancellationToken cancellationToken)
	{
		if (exception is not ValidationException validationException)
			return false;

		var problem = validationException.ToValidationProblemDetails();
		httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
		await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);
		return true;
	}
}
