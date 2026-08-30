using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Aspire;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests.Api;

/// <summary>
/// HTTP API smoke suite hosted by the shared Aspire + WebApplicationFactory fixture.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class FeatureFusionApiTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _client;

	public FeatureFusionApiTests(AspireFixture fixture)
	{
		_client = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Alive_Returns_Healthy()
	{
		var response = await _client.GetAsync("/alive");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
	}

	[Fact]
	public async Task Health_Returns_Healthy()
	{
		var response = await _client.GetAsync("/health");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
	}

	[Theory]
	[InlineData("/api/v1/Auth/login")]
	[InlineData("/api/v2/Auth/login")]
	public async Task Auth_Login_Returns_Jwt(string path)
	{
		using var content = JsonContent.Create(new { username = "vipuser", password = "vippassword" });
		var response = await _client.PostAsync(path, content);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
		payload.Should().NotBeNull();
		payload!.Token.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public async Task Greeting_V1_With_Vip_Jwt_Returns_Custom_Greeting()
	{
		var token = await LoginAsync("/api/v1/Auth/login");
		using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/Greeting/custom-greeting");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		var response = await _client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("VIP");
	}

	[Fact]
	public async Task Greeting_V2_Controller_Accepts_Fullname_Header()
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/Greeting/custom-greeting");
		request.Headers.TryAddWithoutValidation("Fullname", "Mohammad");

		var response = await _client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("user");
	}

	[Fact]
	public async Task Product_Products_First_Page_Returns_Items_And_NextCursor()
	{
		var page = await GetProductsAsync(limit: 5);

		page.Items.Should().HaveCount(5);
		page.HasMore.Should().BeTrue();
		page.NextCursor.Should().NotBeNullOrWhiteSpace();
		page.HasPrevious.Should().BeFalse();
		page.TotalCount.Should().BeGreaterThan(5);
		page.Items.Select(i => i.Id).Should().BeInAscendingOrder();
	}

	[Fact]
	public async Task Product_Products_Next_Page_Via_Cursor_Advances_Without_Overlap()
	{
		var first = await GetProductsAsync(limit: 5);
		first.NextCursor.Should().NotBeNullOrWhiteSpace();

		var second = await GetProductsAsync(limit: 5, cursor: first.NextCursor);

		second.Items.Should().HaveCount(5);
		second.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id));
		second.Items.Min(i => i.Id).Should().BeGreaterThan(first.Items.Max(i => i.Id));
		second.HasPrevious.Should().BeTrue();
		second.PreviousCursor.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public async Task Product_Products_Previous_Cursor_Returns_Earlier_Page()
	{
		var first = await GetProductsAsync(limit: 5);
		var second = await GetProductsAsync(limit: 5, cursor: first.NextCursor);

		var back = await GetProductsAsync(
			limit: 5,
			cursor: second.PreviousCursor,
			sortDirection: "Ascending");

		back.Items.Select(i => i.Id).Should().Equal(first.Items.Select(i => i.Id));
	}

	[Theory]
	[InlineData("Id", "Ascending")]
	[InlineData("Id", "Descending")]
	[InlineData("Name", "Ascending")]
	[InlineData("Name", "Descending")]
	[InlineData("Price", "Ascending")]
	[InlineData("Price", "Descending")]
	[InlineData("CreatedAt", "Ascending")]
	[InlineData("CreatedAt", "Descending")]
	public async Task Product_Products_First_Page_Each_Sort_Field(string sortBy, string sortDirection)
	{
		var page = await GetProductsAsync(limit: 5, sortBy: sortBy, sortDirection: sortDirection);

		page.Items.Should().HaveCount(5);
		page.HasMore.Should().BeTrue();
		page.NextCursor.Should().NotBeNullOrWhiteSpace();
		page.HasPrevious.Should().BeFalse();
		page.TotalCount.Should().BeGreaterThan(5);
	}

	[Fact]
	public async Task Product_Products_Dapper_First_Page_Matches_Ef_Ids()
	{
		var ef = await GetProductsAsync(limit: 5, sortBy: "Price", sortDirection: "Descending");
		var dapper = await GetProductsDapperAsync(limit: 5, sortBy: "Price", sortDirection: "Descending");

		dapper.Items.Select(i => i.Id).Should().Equal(ef.Items.Select(i => i.Id));
		dapper.HasMore.Should().BeTrue();
		dapper.TotalCount.Should().BeGreaterThan(5);
	}

	[Fact]
	public async Task Product_Products_MinimalApi_Get_First_Page_Matches_Controller()
	{
		var controller = await GetProductsAsync(limit: 5, sortBy: "Price", sortDirection: "Descending");
		var minimal = await GetProductsMinimalAsync(limit: 5, sortBy: "Price", sortDirection: "Descending");

		minimal.Items.Select(i => i.Id).Should().Equal(controller.Items.Select(i => i.Id));
		minimal.HasMore.Should().BeTrue();
		minimal.NextCursor.Should().NotBeNullOrWhiteSpace();
		minimal.TotalCount.Should().BeGreaterThan(5);
	}

	[Fact]
	public async Task Product_Products_Get_First_Next_Previous()
	{
		var first = await GetProductsMinimalAsync(limit: 5, sortBy: "Price", sortDirection: "Ascending");

		first.Items.Should().HaveCount(5);
		first.HasMore.Should().BeTrue();
		first.HasPrevious.Should().BeFalse();
		first.TotalCount.Should().BeGreaterThan(5);
		first.NextCursor.Should().NotBeNullOrWhiteSpace();
		first.Items.Select(i => i.Price).Should().BeInAscendingOrder();

		var next = await GetProductsMinimalAsync(
			limit: 5,
			cursor: first.NextCursor,
			sortBy: "Price",
			sortDirection: "Ascending");

		next.Items.Should().HaveCount(5);
		next.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id));
		next.HasPrevious.Should().BeTrue();
		next.PreviousCursor.Should().NotBeNullOrWhiteSpace();

		var back = await GetProductsMinimalAsync(
			limit: 5,
			cursor: next.PreviousCursor,
			sortBy: "Price",
			sortDirection: "Ascending");

		back.Items.Select(i => i.Id).Should().Equal(first.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Product_Products_Get_Last_Page_Via_PageDirection_Backward()
	{
		var last = await GetProductsMinimalAsync(
			limit: 5,
			sortBy: "Id",
			sortDirection: "Ascending",
			pageDirection: "Backward");

		last.Items.Should().HaveCount(5);
		last.HasMore.Should().BeFalse();
		last.HasPrevious.Should().BeTrue();
		last.PreviousCursor.Should().NotBeNullOrWhiteSpace();
		last.TotalCount.Should().BeGreaterThan(5);
		last.Items.Select(i => i.Id).Should().BeInAscendingOrder();

		var earlier = await GetProductsMinimalAsync(
			limit: 5,
			cursor: last.PreviousCursor,
			sortBy: "Id",
			sortDirection: "Ascending");

		earlier.Items.Should().HaveCount(5);
		earlier.Items.Max(i => i.Id).Should().BeLessThan(last.Items.Min(i => i.Id));
	}

	[Fact]
	public async Task Product_Products_Get_Matches_Post()
	{
		var get = await GetProductsMinimalAsync(limit: 5, sortBy: "Price", sortDirection: "Descending");
		var post = await GetProductsMinimalPostAsync(limit: 5, sortBy: "Price", sortDirection: "Descending");

		get.Items.Select(i => i.Id).Should().Equal(post.Items.Select(i => i.Id));
		get.HasMore.Should().Be(post.HasMore);
		get.TotalCount.Should().Be(post.TotalCount);
	}

	[Fact]
	public async Task Product_Products_MinimalApi_Invalid_Cursor_Returns_BadRequest()
	{
		var get = await _client.GetAsync(
			"/api/v2/products-page?Limit=5&Cursor=not-a-valid-cursor");
		get.StatusCode.Should().Be(HttpStatusCode.BadRequest, await get.Content.ReadAsStringAsync());

		var post = await _client.PostAsync(
			"/api/v2/products-page?Limit=5&Cursor=not-a-valid-cursor",
			content: null);
		post.StatusCode.Should().Be(HttpStatusCode.BadRequest, await post.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task Swagger_V2_Documents_Get_Products_Page()
	{
		var response = await _client.GetAsync("/swagger/v2/swagger.json");
		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

		using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		doc.RootElement.TryGetProperty("paths", out var paths).Should().BeTrue();
		paths.TryGetProperty("/api/v2/products-page", out var productsPage).Should().BeTrue();
		productsPage.TryGetProperty("get", out var get).Should().BeTrue();
		productsPage.TryGetProperty("post", out _).Should().BeTrue();

		var getJson = get.GetRawText();
		getJson.Should().Contain("opaque");
		getJson.Contains("limit", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
		getJson.Contains("sortBy", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
		getJson.Contains("pageDirection", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
	}

	[Fact]
	public async Task Product_Products_Sort_By_Price_Descending()
	{
		var page = await GetProductsAsync(limit: 10, sortBy: "Price", sortDirection: "Descending");

		page.Items.Should().HaveCount(10);
		page.Items.Select(i => i.Price).Should().BeInDescendingOrder();
	}

	[Fact]
	public async Task Product_Products_Invalid_Cursor_Returns_BadRequest()
	{
		var response = await _client.PostAsync(
			"/api/v2/Product/products?Limit=5&Cursor=not-a-valid-cursor",
			content: null);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Order_Create_With_Idempotency_Key_Succeeds()
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/Order/order");
		request.Headers.TryAddWithoutValidation("Idempotency-Key", System.Ulid.NewUlid().ToString());
		request.Content = new StringContent(
			"""{"productId":1,"quantity":1,"customerId":1}""",
			Encoding.UTF8,
			"application/json");

		var response = await _client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("orderId");
	}

	[Fact]
	public async Task Minimal_Product_Promotion_Returns_Ok()
	{
		var response = await _client.GetAsync("/api/v2/product-promotion");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Minimal_Product_Recommendation_Returns_Ok()
	{
		var response = await _client.GetAsync("/api/v2/product-recommendation");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("product");
	}

	[Fact]
	public async Task Minimal_Custom_Greeting_Accepts_Fullname_Header()
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/minimal-custom-greeting");
		request.Headers.TryAddWithoutValidation("Fullname", "Mohammad");

		var response = await _client.SendAsync(request);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Theory]
	[InlineData("/api/v2/person-endpointfilter")]
	[InlineData("/api/v2/person-builderextension")]
	[InlineData("/api/v2/person-genericendpoint")]
	public async Task Minimal_Person_Endpoints_Bind_AsParameters_From_Query(string path)
	{
		var response = await _client.PostAsync($"{path}?Name=Mohammad&Age=30", content: null);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadAsStringAsync()).Should().Contain("Mohammad");
	}

	private async Task<ProductsPage> GetProductsAsync(
		int limit,
		string? cursor = null,
		string sortBy = "Id",
		string sortDirection = "Ascending")
	{
		var url = $"/api/v2/Product/products?Limit={limit}&SortBy={sortBy}&SortDirection={sortDirection}";
		if (!string.IsNullOrEmpty(cursor))
		{
			url += $"&Cursor={Uri.EscapeDataString(cursor)}";
		}

		var response = await _client.PostAsync(url, content: null);
		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

		var page = await response.Content.ReadFromJsonAsync<ProductsPage>(JsonOptions);
		page.Should().NotBeNull();
		return page!;
	}

	private async Task<ProductsPage> GetProductsMinimalAsync(
		int limit,
		string? cursor = null,
		string sortBy = "Id",
		string sortDirection = "Ascending",
		string? pageDirection = null)
	{
		var url = ProductsPageUrl(limit, cursor, sortBy, sortDirection, pageDirection);
		var response = await _client.GetAsync(url);
		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

		var page = await response.Content.ReadFromJsonAsync<ProductsPage>(JsonOptions);
		page.Should().NotBeNull();
		return page!;
	}

	private async Task<ProductsPage> GetProductsMinimalPostAsync(
		int limit,
		string? cursor = null,
		string sortBy = "Id",
		string sortDirection = "Ascending")
	{
		var url = ProductsPageUrl(limit, cursor, sortBy, sortDirection);
		var response = await _client.PostAsync(url, content: null);
		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

		var page = await response.Content.ReadFromJsonAsync<ProductsPage>(JsonOptions);
		page.Should().NotBeNull();
		return page!;
	}

	private static string ProductsPageUrl(
		int limit,
		string? cursor,
		string sortBy,
		string sortDirection,
		string? pageDirection = null)
	{
		var url = $"/api/v2/products-page?limit={limit}&sortBy={sortBy}&sortDirection={sortDirection}";
		if (!string.IsNullOrEmpty(cursor))
		{
			url += $"&cursor={Uri.EscapeDataString(cursor)}";
		}

		if (!string.IsNullOrEmpty(pageDirection))
		{
			url += $"&pageDirection={pageDirection}";
		}

		return url;
	}

	private async Task<ProductsPage> GetProductsDapperAsync(
		int limit,
		string? cursor = null,
		string sortBy = "Id",
		string sortDirection = "Ascending")
	{
		var url = $"/api/v2/Product/products-dapper?Limit={limit}&SortBy={sortBy}&SortDirection={sortDirection}";
		if (!string.IsNullOrEmpty(cursor))
		{
			url += $"&Cursor={Uri.EscapeDataString(cursor)}";
		}

		var response = await _client.PostAsync(url, content: null);
		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

		var page = await response.Content.ReadFromJsonAsync<ProductsPage>(JsonOptions);
		page.Should().NotBeNull();
		return page!;
	}

	private async Task<string> LoginAsync(string path)
	{
		using var content = JsonContent.Create(new { username = "vipuser", password = "vippassword" });
		var response = await _client.PostAsync(path, content);
		response.EnsureSuccessStatusCode();
		var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
		return payload!.Token;
	}

	private sealed record LoginResponse(string Token);

	private sealed record ProductsPage(
		IReadOnlyList<ProductItem> Items,
		string NextCursor,
		string PreviousCursor,
		bool HasMore,
		bool HasPrevious,
		int TotalCount);

	private sealed record ProductItem(int Id, string Name, decimal Price, DateTime CreatedAt);
}
