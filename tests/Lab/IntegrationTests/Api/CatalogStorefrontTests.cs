using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FeatureFusion.Infrastructure.Seeding;
using FluentAssertions;
using IntegrationTests.Aspire;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests.Api;

/// <summary>
/// Storefront catalog listing and detail (OFFSET paging at /api/v1/catalog/*).
/// Distinct from keyset Pagination lab routes (/api/v1/products-page, POST /api/v1/Product/products).
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class CatalogStorefrontTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _client;

	public CatalogStorefrontTests(AspireFixture fixture)
	{
		_client = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task List_products_returns_first_page()
	{
		var response = await _client.GetAsync("/api/v1/catalog/products");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var page = await response.Content.ReadFromJsonAsync<ListResponse>(JsonOptions);
		page.Should().NotBeNull();
		page!.Page.Should().Be(1);
		page.PageSize.Should().Be(24);
		page.TotalCount.Should().Be(DemoCommerceSeed.ExpectedProductCount);
		page.Items.Should().HaveCount(24);
		page.Items.Should().OnlyContain(i =>
			!string.IsNullOrWhiteSpace(i.Slug)
			&& !string.IsNullOrWhiteSpace(i.Sku)
			&& !string.IsNullOrWhiteSpace(i.BrandSlug)
			&& !string.IsNullOrWhiteSpace(i.CategorySlug)
			&& !string.IsNullOrWhiteSpace(i.PrimaryImageUrl));
	}

	[Fact]
	public async Task List_products_filters_by_brand_slug()
	{
		var response = await _client.GetAsync($"/api/v1/catalog/products?brand={DemoCommerceSeed.FlagshipBrandSlug}&pageSize=48");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var page = await response.Content.ReadFromJsonAsync<ListResponse>(JsonOptions);
		page.Should().NotBeNull();
		page!.TotalCount.Should().BeGreaterThan(0);
		page.Items.Should().NotBeEmpty();
		page.Items.Should().OnlyContain(i => i.BrandSlug == DemoCommerceSeed.FlagshipBrandSlug);
	}

	[Fact]
	public async Task List_products_filters_by_category_slug()
	{
		var response = await _client.GetAsync($"/api/v1/catalog/products?category={DemoCommerceSeed.FlagshipCategorySlug}&pageSize=48");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var page = await response.Content.ReadFromJsonAsync<ListResponse>(JsonOptions);
		page.Should().NotBeNull();
		page!.Items.Should().NotBeEmpty();
		page.Items.Should().OnlyContain(i => i.CategorySlug == DemoCommerceSeed.FlagshipCategorySlug);
	}

	[Fact]
	public async Task List_products_unknown_brand_returns_empty_page()
	{
		var response = await _client.GetAsync("/api/v1/catalog/products?brand=no-such-brand");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var page = await response.Content.ReadFromJsonAsync<ListResponse>(JsonOptions);
		page.Should().NotBeNull();
		page!.TotalCount.Should().Be(0);
		page.Items.Should().BeEmpty();
		page.Page.Should().Be(1);
		page.PageSize.Should().Be(24);
	}

	[Fact]
	public async Task List_products_clamps_oversized_page_size()
	{
		var response = await _client.GetAsync("/api/v1/catalog/products?pageSize=999");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var page = await response.Content.ReadFromJsonAsync<ListResponse>(JsonOptions);
		page.Should().NotBeNull();
		page!.PageSize.Should().Be(24);
		page.Items.Should().HaveCount(24);
	}

	[Fact]
	public async Task List_related_products_same_category_excludes_source()
	{
		var response = await _client.GetAsync($"/api/v1/catalog/products/{DemoCommerceSeed.FlagshipSlug}/related");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var related = await response.Content.ReadFromJsonAsync<List<RelatedResponse>>(JsonOptions);
		related.Should().NotBeNull();
		related!.Should().NotBeEmpty();
		related.Should().OnlyContain(r => r.Slug != DemoCommerceSeed.FlagshipSlug);
		related.Should().BeInAscendingOrder(r => r.Name);
	}

	[Fact]
	public async Task List_related_unknown_slug_returns_not_found()
	{
		var response = await _client.GetAsync("/api/v1/catalog/products/does-not-exist/related");
		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Get_product_by_slug_returns_gallery_and_specs()
	{
		var response = await _client.GetAsync($"/api/v1/catalog/products/{DemoCommerceSeed.FlagshipSlug}");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var detail = await response.Content.ReadFromJsonAsync<DetailResponse>(JsonOptions);
		detail.Should().NotBeNull();
		detail!.Slug.Should().Be(DemoCommerceSeed.FlagshipSlug);
		detail.Sku.Should().Be(DemoCommerceSeed.FlagshipSku);
		detail.BrandSlug.Should().Be(DemoCommerceSeed.FlagshipBrandSlug);
		detail.CategorySlug.Should().Be(DemoCommerceSeed.FlagshipCategorySlug);
		detail.Images.Should().HaveCountGreaterThanOrEqualTo(3);
		detail.Images.Should().ContainSingle(i => i.IsPrimary);
		detail.Specifications.Should().HaveCountGreaterThanOrEqualTo(3);
		detail.Related.Should().NotBeEmpty();
		detail.Related.Should().OnlyContain(r => r.Slug != detail.Slug);
	}

	[Fact]
	public async Task Get_product_unknown_slug_returns_not_found()
	{
		var response = await _client.GetAsync("/api/v1/catalog/products/does-not-exist");
		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task List_brands_matches_seed()
	{
		var response = await _client.GetAsync("/api/v1/catalog/brands");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var brands = await response.Content.ReadFromJsonAsync<List<BrandResponse>>(JsonOptions);
		brands.Should().NotBeNull();
		brands!.Should().HaveCount(DemoCommerceSeed.ExpectedBrandCount);
		brands.Should().OnlyContain(b => !string.IsNullOrWhiteSpace(b.Slug) && b.ProductCount > 0);
		brands.Should().Contain(b => b.Slug == DemoCommerceSeed.FlagshipBrandSlug && !string.IsNullOrWhiteSpace(b.LogoUrl));
	}

	[Fact]
	public async Task List_categories_matches_seed()
	{
		var response = await _client.GetAsync("/api/v1/catalog/categories");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var categories = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>(JsonOptions);
		categories.Should().NotBeNull();
		categories!.Should().HaveCount(DemoCommerceSeed.ExpectedCategoryCount);
		categories.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Slug) && c.ProductCount > 0);
	}

	private sealed record ListResponse(List<ListItem> Items, int Page, int PageSize, int TotalCount);

	private sealed record ListItem(
		int Id,
		string Name,
		string Slug,
		string Sku,
		decimal Price,
		int StockQuantity,
		bool InStock,
		string BrandName,
		string BrandSlug,
		string CategoryName,
		string CategorySlug,
		string? PrimaryImageUrl,
		string? ShortDescription);

	private sealed record DetailResponse(
		int Id,
		string Name,
		string Slug,
		string Sku,
		decimal Price,
		int StockQuantity,
		bool InStock,
		string? ShortDescription,
		string? FullDescription,
		string BrandName,
		string BrandSlug,
		string CategoryName,
		string CategorySlug,
		List<ImageResponse> Images,
		List<SpecResponse> Specifications,
		List<RelatedResponse> Related);

	private sealed record ImageResponse(string Url, string AltText, bool IsPrimary, int DisplayOrder);

	private sealed record SpecResponse(string Name, string Value, int DisplayOrder);

	private sealed record RelatedResponse(int Id, string Name, string Slug, decimal Price, string? PrimaryImageUrl);

	private sealed record BrandResponse(int Id, string Name, string Slug, string? LogoUrl, int ProductCount);

	private sealed record CategoryResponse(int Id, string Name, string Slug, int ProductCount);
}
