using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FeatureFusion.Domain.Orders;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.Seeding;
using FluentAssertions;
using IntegrationTests.Aspire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.Api;

/// <summary>
/// Demo Commerce order reads under /api/v1/orders (keyset list + detail).
/// Distinct from POST /api/v1/Order/order create.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class OrdersApiTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly HttpClient _client;

	public OrdersApiTests(AspireFixture fixture)
	{
		_fixture = fixture;
		_client = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task List_orders_returns_keyset_first_page()
	{
		var response = await _client.GetAsync("/api/v1/orders?limit=15");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var page = await response.Content.ReadFromJsonAsync<CursorPageResponse<OrderItem>>(JsonOptions);
		page.Should().NotBeNull();
		page!.Items.Should().HaveCount(15);
		// Seed has 50; runtime CreateOrder may add ORD-{Ulid} rows in the shared Aspire DB.
		page.TotalCount.Should().BeGreaterThanOrEqualTo(DemoCommerceSeed.ExpectedOrderCount);
		page.HasMore.Should().BeTrue();
		page.NextCursor.Should().NotBeNullOrWhiteSpace();
		page.Items.Should().OnlyContain(o =>
			!string.IsNullOrWhiteSpace(o.OrderNumber)
			&& o.LineCount >= 1
			&& o.Total > 0);
	}

	[Fact]
	public async Task List_orders_walks_next_cursor()
	{
		var first = await _client.GetFromJsonAsync<CursorPageResponse<OrderItem>>(
			"/api/v1/orders?limit=8",
			JsonOptions);
		first.Should().NotBeNull();

		var second = await _client.GetFromJsonAsync<CursorPageResponse<OrderItem>>(
			$"/api/v1/orders?limit=8&cursor={Uri.EscapeDataString(first!.NextCursor)}",
			JsonOptions);
		second.Should().NotBeNull();
		second!.Items.Select(i => i.Id).Should().NotIntersectWith(first.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Get_order_returns_high_value_order_with_lines()
	{
		// Resolve by seed order number — list first-page can be dominated by newer CreateOrder rows.
		int highId;
		await using (var scope = _fixture.Services.CreateAsyncScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
			highId = (int)(await db.Orders.AsNoTracking()
				.SingleAsync(o => o.OrderNumber == OrderNumber.Create(DemoCommerceSeed.HighValueOrderNumber))).Id;
		}

		var response = await _client.GetAsync($"/api/v1/orders/{highId}");
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var detail = await response.Content.ReadFromJsonAsync<OrderDetail>(JsonOptions);
		detail.Should().NotBeNull();
		detail!.OrderNumber.Should().Be(DemoCommerceSeed.HighValueOrderNumber);
		detail.Status.Should().Be("Pending");
		detail.Total.Should().BeGreaterThan(5000m);
		detail.Lines.Should().HaveCountGreaterThanOrEqualTo(4);
		detail.Lines.Should().OnlyContain(l =>
			l.ProductId > 0
			&& l.Quantity > 0
			&& l.UnitPrice > 0
			&& !string.IsNullOrWhiteSpace(l.ProductName)
			&& !string.IsNullOrWhiteSpace(l.ProductSku));
	}

	[Fact]
	public async Task Get_order_missing_returns_not_found()
	{
		var response = await _client.GetAsync("/api/v1/orders/999999");
		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	private sealed record CursorPageResponse<T>(
		List<T> Items,
		string NextCursor,
		string PreviousCursor,
		bool HasMore,
		bool HasPrevious,
		int TotalCount);

	private sealed record OrderItem(
		int Id,
		string OrderNumber,
		int CustomerId,
		string Status,
		decimal Total,
		string Currency,
		DateTime CreatedAt,
		int LineCount);

	private sealed record OrderDetail(
		int Id,
		string OrderNumber,
		int CustomerId,
		string? CustomerEmail,
		string? CustomerDisplayName,
		string Status,
		decimal Subtotal,
		decimal TaxAmount,
		decimal ShippingAmount,
		decimal Total,
		string Currency,
		DateTime CreatedAt,
		List<OrderLine> Lines);

	private sealed record OrderLine(
		int ProductId,
		string? ProductName,
		string? ProductSlug,
		string? ProductSku,
		int Quantity,
		decimal UnitPrice,
		decimal LineTotal);
}
