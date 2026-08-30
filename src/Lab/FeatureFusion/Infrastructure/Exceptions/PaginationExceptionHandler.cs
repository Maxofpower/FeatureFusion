using BuildingBlocks.Pagination;
using Microsoft.AspNetCore.Diagnostics;

namespace FeatureFusion.Infrastructure.Exceptions;

/// <summary>Maps <see cref="PaginationException"/> to HTTP 400.</summary>
public sealed class PaginationExceptionHandler : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(
		HttpContext httpContext,
		Exception exception,
		CancellationToken cancellationToken)
	{
		if (exception is not PaginationException paginationException)
			return false;

		httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
		await httpContext.Response.WriteAsJsonAsync(
			new HttpValidationProblemDetails(new Dictionary<string, string[]>
			{
				["Code"] = [paginationException.Code.ToString()],
				["Cursor"] = [paginationException.Message]
			})
			{
				Status = StatusCodes.Status400BadRequest,
				Title = "Invalid pagination request",
				Detail = paginationException.Message
			},
			cancellationToken).ConfigureAwait(false);
		return true;
	}
}
