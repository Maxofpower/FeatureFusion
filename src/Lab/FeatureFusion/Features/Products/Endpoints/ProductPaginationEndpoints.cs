using Asp.Versioning;
using BuildingBlocks.Mediator;
using FeatureFusion.Controllers.V2;
using FeatureFusion.Dtos;
using FeatureFusion.Dtos.Validator;
using FeatureFusion.Features.Products.Queries;
using FeatureFusion.Infrastructure.CursorPagination;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Products.Endpoints;

/// <summary>
/// Minimal API surface for keyset pagination (same <see cref="GetProductsQuery"/> as
/// <c>POST /api/v2/Product/products</c> and MCP <c>products.list</c>).
/// </summary>
public static class ProductPaginationEndpoints
{
	private const string CatalogDescription =
		"Keyset (cursor) pagination over the PostgreSQL product catalog via BuildingBlocks.Pagination. " +
		"Query: limit (1–100, default 20), sortBy (Id | Name | Price | CreatedAt), " +
		"sortDirection (Ascending | Descending), optional opaque cursor, optional pageDirection (Forward | Backward). " +
		"Cursors are opaque — send NextCursor or PreviousCursor back unchanged; do not construct cursor contents. " +
		"Empty cursor + Forward (default) is the first page (includes TotalCount). " +
		"Empty cursor + pageDirection=Backward is the last page. " +
		"Same GetProductsQuery as POST /api/v2/Product/products (MVC EF), POST /api/v2/Product/products-dapper, and MCP products.list.";

	public static RouteGroupBuilder MapProductPaginationEndpoints(this IEndpointRouteBuilder app)
	{
		var v2 = new ApiVersion(2, 0);
		var apiVersionSet = app.NewApiVersionSet()
			.HasApiVersion(v2)
			.ReportApiVersions()
			.Build();

		var api = app.MapGroup("api/v{version:apiVersion}")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(v2)
			.WithTags("Products");

		api.MapGet("/products-page", ListAsync)
			.WithName("GetProductsPage")
			.WithSummary("GET catalog page (keyset / cursor). Same GetProductsQuery as MVC, Dapper, and MCP.")
			.WithDescription(CatalogDescription)
			.Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status500InternalServerError);

		api.MapPost("/products-page", ListAsync)
			.WithName("ProductsPage")
			.WithSummary("POST catalog page (same query as GET /api/v2/products-page; kept for compatibility).")
			.WithDescription(CatalogDescription)
			.Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status500InternalServerError);

		return api;
	}

	private static async Task<Results<Ok<PagedResult<ProductDto>>, BadRequest<ValidationProblemDetails>, ProblemHttpResult>> ListAsync(
		GetProductsCommandValidator validator,
		ISender sender,
		CancellationToken cancellationToken,
		[FromQuery] int limit = 20,
		[FromQuery] string cursor = "",
		[FromQuery] ProductSortField sortBy = ProductSortField.Id,
		[FromQuery] SortDirection sortDirection = SortDirection.Ascending,
		[FromQuery] PageDirection pageDirection = PageDirection.Forward)
	{
		var query = new GetProductsQuery
		{
			Limit = limit,
			Cursor = cursor ?? string.Empty,
			SortBy = sortBy,
			SortDirection = sortDirection,
			PageDirection = pageDirection
		};

		var validationResult = await validator.ValidateWithResultAsync(query).ConfigureAwait(false);
		if (validationResult.HasErrors())
		{
			return TypedResults.BadRequest(validationResult.ProblemDetails);
		}

		var result = await sender.Send(query, cancellationToken).ConfigureAwait(false);
		return result.ToHttpResult();
	}
}
