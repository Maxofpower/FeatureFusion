using BuildingBlocks.Mediator;
using FeatureFusion.Dtos;
using FeatureFusion.Dtos.Validator;
using FeatureFusion.Features.Products.Queries;
using FeatureFusion.Infrastructure.CursorPagination;
using FeatureFusion.Infrastructure.Extensions;
using FeatureFusion.Services.ProductService;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Products.Endpoints;

/// <summary>
/// Pagination lab keyset surface (same <see cref="GetProductsQuery"/> as
/// <c>POST /api/v1/Product/products</c> and MCP <c>products.list</c>).
/// Distinct from Demo Commerce storefront <c>GET /api/v1/catalog/products</c>.
/// </summary>
public static class ProductPaginationEndpoints
{
	private const string CatalogDescription =
		"Pagination lab: keyset (cursor) paging over products. " +
		"Not the Demo Commerce storefront (GET /api/v1/catalog/products). " +
		"Query: limit (1–100, default 20), sortBy (Id | Name | Price | CreatedAt), " +
		"sortDirection (Ascending | Descending), optional opaque cursor, optional pageDirection (Forward | Backward). " +
		"Empty cursor + Forward is the first page (includes TotalCount). " +
		"Same query as POST /api/v1/Product/products and POST /api/v1/Product/products-dapper.";

	public static IEndpointRouteBuilder MapProductPaginationEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();
		var v1 = ApiVersioningExtensions.Current;

		var root = app.MapGroup("api/v{version:apiVersion}")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(v1)
			.WithTags("Products");

		root.MapGet("/products-page", ListEfAsync)
			.WithName("GetProductsPage")
			.WithSummary("GET products page (keyset / cursor). Pagination lab, not storefront catalog.")
			.WithDescription(CatalogDescription)
			.Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status500InternalServerError);

		root.MapPost("/products-page", ListEfAsync)
			.WithName("ProductsPage")
			.WithSummary("POST products page (same query as GET /api/v1/products-page).")
			.WithDescription(CatalogDescription)
			.Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status500InternalServerError);

		var product = app.MapGroup("api/v{version:apiVersion}/Product")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(v1)
			.WithTags("Products");

		product.MapPost("/products", ListEfAsync)
			.WithName("PostProductProducts")
			.WithSummary("Pagination lab: keyset page (same GetProductsQuery as GET /api/v1/products-page).")
			.WithDescription(CatalogDescription)
			.Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status500InternalServerError);

		product.MapPost("/products-dapper", ListDapperAsync)
			.WithName("PostProductProductsDapper")
			.WithSummary("Pagination lab: same products table via Dapper (EF is the main list path).")
			.WithDescription(CatalogDescription)
			.Produces<PagedResult<ProductDto>>(StatusCodes.Status200OK)
			.ProducesValidationProblem()
			.ProducesProblem(StatusCodes.Status500InternalServerError);

		return app;
	}

	private static async Task<Results<Ok<PagedResult<ProductDto>>, BadRequest<ValidationProblemDetails>, ProblemHttpResult>> ListEfAsync(
		GetProductsCommandValidator validator,
		ISender sender,
		CancellationToken cancellationToken,
		[FromQuery] int limit = 20,
		[FromQuery] string cursor = "",
		[FromQuery] ProductSortField sortBy = ProductSortField.Id,
		[FromQuery] SortDirection sortDirection = SortDirection.Ascending,
		[FromQuery] PageDirection pageDirection = PageDirection.Forward)
	{
		var query = BuildQuery(limit, cursor, sortBy, sortDirection, pageDirection);
		var validationResult = await validator.ValidateWithResultAsync(query).ConfigureAwait(false);
		if (validationResult.HasErrors())
			return TypedResults.BadRequest(validationResult.ProblemDetails);

		var result = await sender.Send(query, cancellationToken).ConfigureAwait(false);
		return result.ToHttpResult();
	}

	private static async Task<Results<Ok<PagedResult<ProductDto>>, BadRequest<ValidationProblemDetails>, ProblemHttpResult>> ListDapperAsync(
		GetProductsCommandValidator validator,
		IProductService products,
		CancellationToken cancellationToken,
		[FromQuery] int limit = 20,
		[FromQuery] string cursor = "",
		[FromQuery] ProductSortField sortBy = ProductSortField.Id,
		[FromQuery] SortDirection sortDirection = SortDirection.Ascending,
		[FromQuery] PageDirection pageDirection = PageDirection.Forward)
	{
		var query = BuildQuery(limit, cursor, sortBy, sortDirection, pageDirection);
		var validationResult = await validator.ValidateWithResultAsync(query).ConfigureAwait(false);
		if (validationResult.HasErrors())
			return TypedResults.BadRequest(validationResult.ProblemDetails);

		var result = await products.GetProductsViaDapperAsync(
			query.Limit,
			query.SortBy,
			query.SortDirection,
			query.Cursor,
			(BuildingBlocks.Pagination.PageDirection)query.PageDirection,
			cancellationToken).ConfigureAwait(false);

		return result.ToHttpResult();
	}

	private static GetProductsQuery BuildQuery(
		int limit,
		string? cursor,
		ProductSortField sortBy,
		SortDirection sortDirection,
		PageDirection pageDirection) =>
		new()
		{
			Limit = limit,
			Cursor = cursor ?? string.Empty,
			SortBy = sortBy,
			SortDirection = sortDirection,
			PageDirection = pageDirection
		};
}
