using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FeatureFusion.Infrastructure.Seeding;
using FluentAssertions;
using IntegrationTests.Aspire;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests.Api;

/// <summary>
/// Demo Commerce customer reads. List uses BuildingBlocks.Pagination keyset;
/// nested orders use OFFSET.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class CustomersApiTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _client;

	public CustomersApiTests(AspireFixture fixture)
	{
		_client = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task List_customers_returns_keyset_first_page()
	{
		var response = await _client.GetAsync("/api/v1/customers?limit=10");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var page = await response.Content.ReadFromJsonAsync<CursorPageResponse<CustomerItem>>(JsonOptions);
		page.Should().NotBeNull();
		page!.Items.Should().HaveCount(10);
		page.TotalCount.Should().Be(DemoCommerceSeed.ExpectedCustomerCount);
		page.HasMore.Should().BeTrue();
		page.NextCursor.Should().NotBeNullOrWhiteSpace();
		page.Items.Should().OnlyContain(c =>
			!string.IsNullOrWhiteSpace(c.Email) && !string.IsNullOrWhiteSpace(c.DisplayName));
	}

	[Fact]
	public async Task List_customers_walks_next_cursor()
	{
		var first = await _client.GetFromJsonAsync<CursorPageResponse<CustomerItem>>(
			"/api/v1/customers?limit=5",
			JsonOptions);
		first.Should().NotBeNull();
		first!.NextCursor.Should().NotBeNullOrWhiteSpace();

		var second = await _client.GetFromJsonAsync<CursorPageResponse<CustomerItem>>(
			$"/api/v1/customers?limit=5&cursor={Uri.EscapeDataString(first.NextCursor)}",
			JsonOptions);
		second.Should().NotBeNull();
		second!.Items.Should().HaveCount(5);
		second.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Get_customer_returns_power_customer()
	{
		var response = await _client.GetAsync("/api/v1/customers/1");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var customer = await response.Content.ReadFromJsonAsync<CustomerItem>(JsonOptions);
		customer.Should().NotBeNull();
		customer!.Id.Should().Be(1);
		customer.Email.Should().Be(DemoCommerceSeed.PowerCustomerEmail);
		customer.DisplayName.Should().Be("Alex Power");
	}

	[Fact]
	public async Task Get_customer_missing_returns_not_found()
	{
		var response = await _client.GetAsync("/api/v1/customers/999999");
		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task List_customer_orders_for_power_customer()
	{
		// Power customer is id 1; Exp/CreateOrder may add ORD-{Ulid} rows (newest first).
		var seeded = new List<CustomerOrderItem>();
		var pageNum = 1;
		int totalCount;
		do
		{
			var response = await _client.GetAsync($"/api/v1/customers/1/orders?page={pageNum}&pageSize=50");
			response.StatusCode.Should().Be(HttpStatusCode.OK);

			var page = await response.Content.ReadFromJsonAsync<OffsetPageResponse<CustomerOrderItem>>(JsonOptions);
			page.Should().NotBeNull();
			totalCount = page!.TotalCount;
			seeded.AddRange(page.Items.Where(o =>
				o.OrderNumber.StartsWith("ORD-PWR-", StringComparison.Ordinal)));
			pageNum++;
		} while ((pageNum - 1) * 50 < totalCount && seeded.Count < 6);

		totalCount.Should().BeGreaterThanOrEqualTo(6);
		seeded.Should().HaveCount(6);
		seeded.Should().OnlyContain(o => o.LineCount >= 2);
	}

	[Fact]
	public async Task List_orders_for_missing_customer_returns_not_found()
	{
		var response = await _client.GetAsync("/api/v1/customers/999999/orders");
		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	private sealed record CursorPageResponse<T>(
		List<T> Items,
		string NextCursor,
		string PreviousCursor,
		bool HasMore,
		bool HasPrevious,
		int TotalCount);

	private sealed record OffsetPageResponse<T>(List<T> Items, int Page, int PageSize, int TotalCount);

	private sealed record CustomerItem(int Id, string Email, string DisplayName, DateTime CreatedAt);

	private sealed record CustomerOrderItem(
		int Id,
		string OrderNumber,
		string Status,
		decimal Total,
		string Currency,
		DateTime CreatedAt,
		int LineCount);
}
