namespace FeatureFusion.Infrastructure.Extensions
{
	using Microsoft.AspNetCore.Mvc;
	using System.Collections.Generic;

	public static class ValidationProblemHelper
	{
		/// <summary>
		/// Creates a standardized 400 Bad Request response with a custom error message.
		/// </summary>
		/// <param name="title">The title of the error.</param>
		/// <param name="detail">The detailed error message.</param>
		/// <returns>A <see cref="BadRequestObjectResult"/> with a validation problem details object.</returns>
		public static BadRequestObjectResult CreateBadRequest(string title, string detail)
		{
			var problemDetails = new ValidationProblemDetails
			{
				Title = title,
				Detail = detail,
				Status = 400
			};

			return new BadRequestObjectResult(problemDetails);
		}

		/// <summary>
		/// Creates a standardized 401 Unauthorized response with a custom error message.
		/// </summary>
		/// <param name="title">The title of the error.</param>
		/// <param name="detail">The detailed error message.</param>
		/// <returns>An <see cref="UnauthorizedObjectResult"/> with a validation problem details object.</returns>
		public static UnauthorizedObjectResult CreateUnauthorized(string title, string detail)
		{
			var problemDetails = new ValidationProblemDetails
			{
				Title = title,
				Detail = detail,
				Status = 401
			};

			return new UnauthorizedObjectResult(problemDetails);
		}

		/// <summary>
		/// Creates a standardized 403 Forbidden response with a custom error message.
		/// </summary>
		/// <param name="title">The title of the error.</param>
		/// <param name="detail">The detailed error message.</param>
		/// <returns>A <see cref="ForbidResult"/> with a validation problem details object.</returns>
		public static ObjectResult CreateForbidden(string title, string detail)
		{
			var problemDetails = new ValidationProblemDetails
			{
				Title = title,
				Detail = detail,
				Status = 403
			};

			return new ObjectResult(problemDetails) { StatusCode = 403 };
		}

		/// <summary>
		/// Creates a standardized 404 Not Found response with a custom error message.
		/// </summary>
		/// <param name="title">The title of the error.</param>
		/// <param name="detail">The detailed error message.</param>
		/// <returns>A <see cref="NotFoundObjectResult"/> with a validation problem details object.</returns>
		public static NotFoundObjectResult CreateNotFound(string title, string detail)
		{
			var problemDetails = new ValidationProblemDetails
			{
				Title = title,
				Detail = detail,
				Status = 404
			};

			return new NotFoundObjectResult(problemDetails);
		}

		/// <summary>
		/// Creates a standardized 408 Request Timeout response with a custom error message.
		/// </summary>
		/// <param name="title">The title of the error.</param>
		/// <param name="detail">The detailed error message.</param>
		/// <returns>An <see cref="ObjectResult"/> with a validation problem details object.</returns>
		public static ObjectResult CreateTimeout(string title, string detail)
		{
			var problemDetails = new ValidationProblemDetails
			{
				Title = title,
				Detail = detail,
				Status = 408
			};

			return new ObjectResult(problemDetails) { StatusCode = 408 };
		}

		/// <summary>
		/// Creates a standardized 409 Conflict response with a custom error message.
		/// </summary>
		/// <param name="title">The title of the error.</param>
		/// <param name="detail">The detailed error message.</param>
		/// <returns>A <see cref="ConflictObjectResult"/> with a validation problem details object.</returns>
		public static ConflictObjectResult CreateConflict(string title, string detail)
		{
			var problemDetails = new ValidationProblemDetails
			{
				Title = title,
				Detail = detail,
				Status = 409
			};

			return new ConflictObjectResult(problemDetails);
		}

		/// <summary>
		/// Creates a standardized 500 Internal Server Error response with a custom error message.
		/// </summary>
		/// <param name="title">The title of the error.</param>
		/// <param name="detail">The detailed error message.</param>
		/// <returns>An <see cref="ObjectResult"/> with a validation problem details object.</returns>
		public static ObjectResult CreateErrorResponse(string title, string detail, int statusCode = 500)
		{
			var problemDetails = new ValidationProblemDetails
			{
				Title = title,
				Detail = detail,
				Status = statusCode
			};

			return new ObjectResult(problemDetails) { StatusCode = statusCode };
		}
	}
}
