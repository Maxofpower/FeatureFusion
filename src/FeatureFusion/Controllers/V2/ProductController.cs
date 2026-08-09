using FeatureFusion.Infrastructure.Filters;
using BuildingBlocks.Mediator;
using FeatureFusion.Models;
using FeatureManagementFilters.Models;
using FeatureManagementFilters.Services.FeatureToggleService;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static FeatureFusion.Features.Orders.Commands.CreateOrderCommandHandler;
using FeatureFusion.Dtos;
using FeatureFusion.Infrastructure.CursorPagination;
using FeatureFusion.Dtos.Validator;
using FluentValidation;
using FeatureFusion.Features.Products.Queries;


namespace FeatureFusion.Controllers.V2
{
	[ApiController]
	[Route("api/v{version:apiVersion}/[controller]")]
	public class ProductController : Controller
	{
		private readonly GetProductsCommandValidator _validator;
		private readonly ISender _sender;
		public ProductController(GetProductsCommandValidator validator, ISender sender)
		{
			_validator = validator;
			_sender = sender;
		}

		// with cursor-pagination
		[HttpPost("products")]
		[ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
		[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
		[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
		public async Task<Results<Ok<PagedResult<ProductDto>>,
		BadRequest<ValidationProblemDetails>, ProblemHttpResult>> GetProducts(
		[FromQuery] GetProductsQuery command,
		CancellationToken cancellationToken)
		{
			{
				var validationResult = await _validator.ValidateWithResultAsync(command);
				if (validationResult.HasErrors())
				{
					return TypedResults.BadRequest(validationResult.ProblemDetails);
				}

				var result = await _sender.Send(command, cancellationToken);

				return result.ToHttpResult();
			}
		}

	}

	public static class ResultExtensions
	{
		public static Results<Ok<T>, BadRequest<ValidationProblemDetails>, ProblemHttpResult> ToHttpResult<T>(this Result<T> result)
		{
			return result.Match<Results<Ok<T>, BadRequest<ValidationProblemDetails>, ProblemHttpResult>>(
			success => TypedResults.Ok(success),
			(error, statusCode) =>
			{
				var errors = new Dictionary<string, string[]>
				{
				{ "General", new[] { error } }
				};
				return TypedResults.BadRequest(new ValidationProblemDetails(errors)
				{
					Title = "Request Error",
					Detail = error,
					Status = statusCode
				});
			});
		}
	}
}



