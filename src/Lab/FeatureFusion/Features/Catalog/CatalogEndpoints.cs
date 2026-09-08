using Asp.Versioning;
using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Features.Catalog;

/// <summary>
/// Demo Commerce storefront catalog (OFFSET listing + product detail).
/// Distinct from the Pagination lab: GET/POST /api/v1/products-page and POST /api/v1/Product/products.
/// </summary>
public static class CatalogEndpoints
{
	public static RouteGroupBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
	{
		var apiVersionSet = app.CreateLabApiVersionSet();

		var api = app.MapGroup("api/v{version:apiVersion}/catalog")
			.WithApiVersionSet(apiVersionSet)
			.MapToApiVersion(ApiVersioningExtensions.Current)
			.WithTags("Catalog");

		api.MapGet("/products", ListProductsAsync)
			.WithName("ListCatalogProducts")
			.WithSummary("Storefront product listing (brand/category slug filters). OFFSET paging — not keyset.")
			.WithDescription("Same Mediator query as MCP tool catalog.products.list.")
			.Produces<CatalogProductListDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		api.MapGet("/products/{slug}", GetProductAsync)
			.WithName("GetCatalogProductBySlug")
			.WithSummary("Storefront product detail by slug, including gallery, specs, and related products.")
			.WithDescription("Same Mediator query as MCP tool catalog.product.get.")
			.Produces<CatalogProductDetailDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		api.MapGet("/products/{slug}/related", ListRelatedAsync)
			.WithName("ListRelatedCatalogProducts")
			.WithSummary("Same-category related products (deterministic Name, Id). Not a recommendation engine.")
			.Produces<IReadOnlyList<CatalogRelatedProductDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		api.MapGet("/brands", ListBrandsAsync)
			.WithName("ListCatalogBrands")
			.WithSummary("Brands for listing filters.")
			.Produces<IReadOnlyList<CatalogBrandDto>>(StatusCodes.Status200OK);

		api.MapGet("/categories", ListCategoriesAsync)
			.WithName("ListCatalogCategories")
			.WithSummary("Categories for listing filters.")
			.Produces<IReadOnlyList<CatalogCategoryDto>>(StatusCodes.Status200OK);

		return api;
	}

	private static async Task<IResult> ListProductsAsync(
		ISender sender,
		CancellationToken cancellationToken,
		[FromQuery] string? brand = null,
		[FromQuery] string? category = null,
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 24)
	{
		var result = await sender.Send(
			new ListCatalogProductsQuery
			{
				Brand = brand,
				Category = category,
				Page = page,
				PageSize = pageSize
			},
			cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> GetProductAsync(
		string slug,
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new GetCatalogProductBySlugQuery(slug), cancellationToken)
			.ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> ListRelatedAsync(
		string slug,
		ISender sender,
		CancellationToken cancellationToken,
		[FromQuery] int limit = 8)
	{
		var result = await sender.Send(new ListRelatedCatalogProductsQuery(slug, limit), cancellationToken)
			.ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> ListBrandsAsync(
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new ListCatalogBrandsQuery(), cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}

	private static async Task<IResult> ListCategoriesAsync(
		ISender sender,
		CancellationToken cancellationToken)
	{
		var result = await sender.Send(new ListCatalogCategoriesQuery(), cancellationToken).ConfigureAwait(false);
		return result.ToApiResult();
	}
}
