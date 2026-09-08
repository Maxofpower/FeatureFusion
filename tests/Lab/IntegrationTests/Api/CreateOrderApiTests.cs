using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FeatureFusion.Domain.Catalog;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.Seeding;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace IntegrationTests.Api;

/// <summary>
/// Real CreateOrder vertical slice: domain Order + price snapshot + idempotency + MCP convergence.
/// Path remains <c>POST /api/v1/Order/order</c> for Exp 1–20 compatibility.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class CreateOrderApiTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly HttpClient _http;

	public CreateOrderApiTests(AspireFixture fixture)
	{
		_fixture = fixture;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Happy_path_persists_order_lines_total_and_placed_status()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var result = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 2, productId: 1, customerId: 1);

		result.HttpStatus.Should().Be(200);
		result.OrderId.Should().NotBeEmpty();
		result.Quantity.Should().Be(2);
		result.TotalAmount.Should().Be(2398.00m); // flagship iPhone 1199 × 2

		var created = JsonSerializer.Deserialize<CreateOrderResponse>(result.Body, JsonOptions);
		created.Should().NotBeNull();
		created!.DomainOrderId.Should().BeGreaterThan(0);
		created.OrderNumber.Should().StartWith("ORD-");
		created.Status.Should().Be("Placed");

		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var order = await db.Orders.AsNoTracking()
			.Include(o => o.Items)
			.SingleAsync(o => (int)o.Id == created.DomainOrderId);
		order.OrderNumber.Value.Should().Be(created.OrderNumber);
		order.Status.ToString().Should().Be("Placed");
		order.Total.Should().Be(2398.00m);
		order.Items.Should().ContainSingle();
		order.Items.Single().Quantity.Should().Be(2);
		order.Items.Single().UnitPrice.Should().Be(1199.00m);

		await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, created.DomainOrderId);
	}

	[Fact]
	public async Task Unknown_customer_returns_not_found()
	{
		using var capture = new InProcessActivityCapture();
		var result = await HttpOrderCreate.PostAsync(
			_http, capture, Ulid.NewUlid().ToString(), quantity: 1, productId: 1, customerId: 999999);
		result.HttpStatus.Should().Be(404);
	}

	[Fact]
	public async Task Unknown_product_returns_not_found()
	{
		using var capture = new InProcessActivityCapture();
		var result = await HttpOrderCreate.PostAsync(
			_http, capture, Ulid.NewUlid().ToString(), quantity: 1, productId: 999999, customerId: 1);
		result.HttpStatus.Should().Be(404);
	}

	[Fact]
	public async Task Zero_quantity_returns_bad_request()
	{
		using var capture = new InProcessActivityCapture();
		var result = await HttpOrderCreate.PostAsync(
			_http, capture, Ulid.NewUlid().ToString(), quantity: 0, productId: 1, customerId: 1);
		result.HttpStatus.Should().Be(400);
	}

	[Fact]
	public async Task Out_of_stock_product_returns_conflict()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var oosId = (await db.Product.AsNoTracking().ToListAsync())
			.Single(p => p.Sku.Value == DemoCommerceSeed.OutOfStockSku)
			.Id.Value;

		using var capture = new InProcessActivityCapture();
		var result = await HttpOrderCreate.PostAsync(
			_http, capture, Ulid.NewUlid().ToString(), quantity: 1, productId: oosId, customerId: 1);
		result.HttpStatus.Should().Be(409);
	}

	[Fact]
	public async Task Price_snapshot_survives_catalog_price_change()
	{
		using var capture = new InProcessActivityCapture();
		var create = await HttpOrderCreate.PostAsync(
			_http, capture, Ulid.NewUlid().ToString(), quantity: 1, productId: 2, customerId: 1);
		create.HttpStatus.Should().Be(200);
		var created = JsonSerializer.Deserialize<CreateOrderResponse>(create.Body, JsonOptions)!;
		var originalTotal = created.TotalAmount;

		await using (var scope = _fixture.Services.CreateAsyncScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
			var product = await db.Product.SingleAsync(p => (int)p.Id == 2);
			var before = product.Price;
			product.ChangePrice(before + 250m);
			await db.SaveChangesAsync();

			var order = await db.Orders.AsNoTracking()
				.Include(o => o.Items)
				.SingleAsync(o => (int)o.Id == created.DomainOrderId);
			order.Items.Single().UnitPrice.Should().Be(before);
			order.Total.Should().Be(originalTotal);

			// restore catalog price for other tests
			product = await db.Product.SingleAsync(p => (int)p.Id == 2);
			product.ChangePrice(before);
			await db.SaveChangesAsync();
		}

		var detail = await _http.GetFromJsonAsync<OrderDetailResponse>(
			$"/api/v1/orders/{created.DomainOrderId}", JsonOptions);
		detail.Should().NotBeNull();
		detail!.Total.Should().Be(originalTotal);
		detail.Lines.Should().ContainSingle(l => l.UnitPrice * l.Quantity == originalTotal);

		await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, created.DomainOrderId);
	}

	[Fact]
	public async Task Same_idempotency_key_same_body_replays_without_second_order()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var first = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 1, productId: 3, customerId: 1);
		var second = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 1, productId: 3, customerId: 1);

		first.HttpStatus.Should().Be(200);
		second.HttpStatus.Should().Be(200);
		second.CachedResponseHeader.Should().BeTrue();
		second.OrderId.Should().Be(first.OrderId);

		var created = JsonSerializer.Deserialize<CreateOrderResponse>(first.Body, JsonOptions)!;
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var count = await db.Orders.CountAsync(o => (int)o.Id == created.DomainOrderId);
		count.Should().Be(1);

		await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, created.DomainOrderId);
	}

	[Fact]
	public async Task Same_idempotency_key_different_body_conflicts()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();

		// Fingerprint requires EnableRequestFingerprint — use dedicated host like Exp 12.
		var host = _fixture.WithWebHostBuilder(b =>
		{
			b.ConfigureTestServices(services =>
			{
				services.PostConfigure<BuildingBlocks.Idempotency.IdempotencyOptions>(o =>
				{
					o.EnableRequestFingerprint = true;
				});
			});
		});
		var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

		var first = await HttpOrderCreate.PostAsync(client, capture, key, quantity: 1, productId: 4, customerId: 1);
		var second = await HttpOrderCreate.PostAsync(client, capture, key, quantity: 2, productId: 4, customerId: 1);

		first.HttpStatus.Should().Be(200);
		second.HttpStatus.Should().Be(422);
		second.CachedResponseHeader.Should().BeFalse();

		var created = JsonSerializer.Deserialize<CreateOrderResponse>(first.Body, JsonOptions);
		if (created is not null)
			await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, created.DomainOrderId);
	}

	[Fact]
	public async Task Http_and_mcp_create_orders_through_same_command_shape()
	{
		using var capture = new InProcessActivityCapture();
		var http = await HttpOrderCreate.PostAsync(
			_http, capture, Ulid.NewUlid().ToString(), quantity: 1, productId: 5, customerId: 1);
		http.HttpStatus.Should().Be(200);
		var httpBody = JsonSerializer.Deserialize<CreateOrderResponse>(http.Body, JsonOptions)!;

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var mcpResult = await mcp.CallToolAsync(
			"orders.create",
			new Dictionary<string, object?>
			{
				["productId"] = 5,
				["quantity"] = 1,
				["customerId"] = 1,
				["confirmed"] = true,
				["idempotencyKey"] = Ulid.NewUlid().ToString()
			});

		(mcpResult.IsError ?? false).Should().BeFalse();
		mcpResult.StructuredContent.Should().NotBeNull();
		var mcpBody = JsonSerializer.Deserialize<CreateOrderResponse>(
			mcpResult.StructuredContent!.Value.GetRawText(), JsonOptions)!;

		httpBody.Status.Should().Be("Placed");
		mcpBody.Status.Should().Be("Placed");
		mcpBody.DomainOrderId.Should().BeGreaterThan(0);
		mcpBody.OrderNumber.Should().StartWith("ORD-");
		mcpBody.OrderId.Should().NotBe(httpBody.OrderId);

		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		(await db.Orders.AnyAsync(o => (int)o.Id == httpBody.DomainOrderId)).Should().BeTrue();
		(await db.Orders.AnyAsync(o => (int)o.Id == mcpBody.DomainOrderId)).Should().BeTrue();

		await CreatedOrderCleanup.DeleteByDomainIdsAsync(
			_fixture.Services, httpBody.DomainOrderId, mcpBody.DomainOrderId);
	}

	[Fact]
	public async Task Multi_line_items_payload_creates_merged_lines()
	{
		var key = Ulid.NewUlid().ToString();
		using var request = new HttpRequestMessage(HttpMethod.Post, HttpOrderCreate.Path);
		request.Headers.TryAddWithoutValidation(HttpOrderCreate.IdempotencyHeader, key);
		request.Content = new StringContent(
			"""{"customerId":1,"items":[{"productId":6,"quantity":1},{"productId":7,"quantity":2}]}""",
			Encoding.UTF8,
			"application/json");

		using var response = await _http.SendAsync(request);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions);
		body.Should().NotBeNull();
		body!.DomainOrderId.Should().BeGreaterThan(0);

		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var order = await db.Orders.AsNoTracking()
			.Include(o => o.Items)
			.SingleAsync(o => (int)o.Id == body.DomainOrderId);
		order.Items.Should().HaveCount(2);
		order.Items.Sum(i => i.Quantity).Should().Be(3);

		await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, body.DomainOrderId);
	}

	private sealed record CreateOrderResponse(
		Guid OrderId,
		int DomainOrderId,
		string OrderNumber,
		string Status,
		string CustomerName,
		string ProductName,
		int Quantity,
		decimal TotalAmount,
		DateTime OrderDate,
		string Message);

	private sealed record OrderDetailResponse(
		int Id,
		string OrderNumber,
		decimal Total,
		List<OrderLineResponse> Lines);

	private sealed record OrderLineResponse(int ProductId, int Quantity, decimal UnitPrice, decimal LineTotal);
}
