using FeatureFusion.Dtos;
using FeatureFusion.Features.Order.IntegrationEvents;
using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.CursorPagination;
using FeatureFusion.Services.ProductService;
using static FeatureFusion.Infrastructure.CursorPagination.CursorFactory;

namespace FeatureFusion.Features.Products.Queries
{
	public sealed class GetProductsQueryHandler
	: IQueryHandler<GetProductsQuery, Result<PagedResult<ProductDto>>>
	{
		private readonly IServiceProvider _serviceProvider;

		public GetProductsQueryHandler(IServiceProvider serviceProvider)
		{
			_serviceProvider = serviceProvider;
		}

		public async Task<Result<PagedResult<ProductDto>>> Handle(
			GetProductsQuery request,
			CancellationToken cancellationToken)
		{

			var productService = _serviceProvider.GetRequiredService<IProductService>();
			try
			{
				if (request.Limit <= 0 || request.Limit > 100)
				{
					return Result<PagedResult<ProductDto>>.Failure(
						"Limit must be between 1 and 100",
						StatusCodes.Status400BadRequest);
				}

				var result = await productService.GetProductsAsync(
					request.Limit,
					request.SortBy,
					request.SortDirection,
					request.Cursor,
					cancellationToken);

				return result;
			}
			catch (OperationCanceledException ex)
			{
				return Result<PagedResult<ProductDto>>.Failure(
					$"Request was cancelled : {ex.Message}",
					StatusCodes.Status499ClientClosedRequest);
			}
			catch (Exception ex)
			{
				return Result<PagedResult<ProductDto>>.Failure(
					$"An error occurred while retrieving products : {ex.Message}",
					StatusCodes.Status500InternalServerError);
			}
		}
	}
}
