using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FeatureFusion.Features.Shipping;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.Api;

[Collection(AspireCollection.Name)]
public sealed class CartCheckoutApiTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly HttpClient _http;

	public CartCheckoutApiTests(AspireFixture fixture)
	{
		_fixture = fixture;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
	}

	[Fact]
	public async Task Cart_add_update_remove_clear_roundtrip()
	{
		const int customerId = 3;
		await ClearCartAsync(customerId);

		var empty = await _http.GetFromJsonAsync<CartDto>($"/api/v1/customers/{customerId}/cart", JsonOptions);
		empty!.Items.Should().BeEmpty();

		var add = await _http.PostAsJsonAsync(
			$"/api/v1/customers/{customerId}/cart/items",
			new { productId = 12, quantity = 2 });
		add.StatusCode.Should().Be(HttpStatusCode.OK);
		var afterAdd = await add.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
		afterAdd!.Items.Should().ContainSingle(i => i.ProductId == 12 && i.Quantity == 2);

		var update = await _http.PutAsJsonAsync(
			$"/api/v1/customers/{customerId}/cart/items/12",
			new { quantity = 5 });
		update.StatusCode.Should().Be(HttpStatusCode.OK);
		(await update.Content.ReadFromJsonAsync<CartDto>(JsonOptions))!
			.Items.Should().ContainSingle(i => i.ProductId == 12 && i.Quantity == 5);

		var remove = await _http.DeleteAsync($"/api/v1/customers/{customerId}/cart/items/12");
		remove.StatusCode.Should().Be(HttpStatusCode.OK);
		(await remove.Content.ReadFromJsonAsync<CartDto>(JsonOptions))!.Items.Should().BeEmpty();

		await _http.PostAsJsonAsync(
			$"/api/v1/customers/{customerId}/cart/items",
			new { productId = 12, quantity = 1 });
		var clear = await _http.DeleteAsync($"/api/v1/customers/{customerId}/cart");
		clear.StatusCode.Should().Be(HttpStatusCode.OK);
		(await clear.Content.ReadFromJsonAsync<CartDto>(JsonOptions))!.Items.Should().BeEmpty();
	}

	[Fact]
	public async Task Checkout_success_computes_totals_decrements_stock_and_clears_cart()
	{
		const int customerId = 4;
		const int productId = 12; // MX Master 99.00
		await ClearCartAsync(customerId);

		int stockBefore;
		await using (var scope = _fixture.Services.CreateAsyncScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
			stockBefore = (await db.Product.AsNoTracking().SingleAsync(p => (int)p.Id == productId)).StockQuantity;
		}

		(await _http.PostAsJsonAsync(
			$"/api/v1/customers/{customerId}/cart/items",
			new { productId, quantity = 2 })).EnsureSuccessStatusCode();

		var key = Ulid.NewUlid().ToString();
		using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{customerId}/checkout");
		request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
		request.Content = JsonContent.Create(new
		{
			recipientName = "Dana Pierce",
			line1 = "1 Demo St",
			city = "Berlin",
			postalCode = "10115",
			country = "DE"
		});

		using var response = await _http.SendAsync(request);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync<CheckoutDto>(JsonOptions);
		body.Should().NotBeNull();
		body!.Subtotal.Should().Be(198.00m);
		body.TaxAmount.Should().Be(19.80m); // 10%
		body.ShippingAmount.Should().Be(DemoShippingPolicy.FlatFee);
		body.GrandTotal.Should().Be(198.00m + 19.80m + DemoShippingPolicy.FlatFee);
		body.Status.Should().Be("Placed");
		body.PaymentDecision.Should().Be("Approved");

		var cart = await _http.GetFromJsonAsync<CartDto>($"/api/v1/customers/{customerId}/cart", JsonOptions);
		cart!.Items.Should().BeEmpty();

		await using (var scope = _fixture.Services.CreateAsyncScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
			var stockAfter = (await db.Product.AsNoTracking().SingleAsync(p => (int)p.Id == productId)).StockQuantity;
			stockAfter.Should().Be(stockBefore - 2);

			var order = await db.Orders.AsNoTracking()
				.Include(o => o.Items)
				.SingleAsync(o => (int)o.Id == body.DomainOrderId);
			order.Subtotal.Should().Be(198.00m);
			order.TaxAmount.Should().Be(19.80m);
			order.ShippingAmount.Should().Be(DemoShippingPolicy.FlatFee);
			order.Total.Should().Be(body.GrandTotal);
			order.Items.Single().UnitPrice.Should().Be(99.00m);

			var outbox = await db.OutboxMessages.AsNoTracking()
				.OrderByDescending(o => o.CreatedAt)
				.FirstAsync();
			outbox.EventType.Should().Contain("OrderCreated");

			await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, body.DomainOrderId);
		}
	}

	[Fact]
	public async Task Checkout_empty_cart_returns_bad_request()
	{
		const int customerId = 5;
		await ClearCartAsync(customerId);
		using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{customerId}/checkout");
		request.Headers.TryAddWithoutValidation("Idempotency-Key", Ulid.NewUlid().ToString());
		request.Content = JsonContent.Create(DefaultAddress());
		using var response = await _http.SendAsync(request);
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Checkout_payment_decline_leaves_cart_and_creates_no_order()
	{
		// Craft grand total ending in .13: need subtotal+tax+ship where cents%100==13.
		// Use quantity that produces grand ending .13 via DemoTax 10% + 4.99 shipping.
		// Easier approach: product with price such that (price*1.1 + 4.99) ends with .13
		// For price P: round(P*1.1,2)+4.99 ends with .13
		// Use LOW product carefully — instead force via known math:
		// Find: Demo decline when (grand*100)%100==13.
		// With FlatFee 4.99 and 10% tax: grand = round(sub*1.1,2)+4.99
		// Pick subtotal 10.127... wait use product 99 and qty that works, or add item
		// of price 4.6727... Simpler: use customer cart with product priced so
		// grand cents == 13. Solve: round(S*0.1,2)+S+4.99 = x.13
		// Try S=4.67 → tax 0.47 → 5.14+4.99=10.13 ✓
		// No seed product at 4.67. Use ChangePrice temporarily on product 12 for this customer test.

		const int customerId = 6;
		const int productId = 12;
		await ClearCartAsync(customerId);

		decimal originalPrice;
		await using (var scope = _fixture.Services.CreateAsyncScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
			var product = await db.Product.SingleAsync(p => (int)p.Id == productId);
			originalPrice = product.Price;
			product.ChangePrice(4.67m);
			await db.SaveChangesAsync();
		}

		try
		{
			(await _http.PostAsJsonAsync(
				$"/api/v1/customers/{customerId}/cart/items",
				new { productId, quantity = 1 })).EnsureSuccessStatusCode();

			var ordersBefore = await CountOrdersAsync();
			using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{customerId}/checkout");
			request.Headers.TryAddWithoutValidation("Idempotency-Key", Ulid.NewUlid().ToString());
			request.Content = JsonContent.Create(DefaultAddress());
			using var response = await _http.SendAsync(request);
			response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

			(await CountOrdersAsync()).Should().Be(ordersBefore);
			var cart = await _http.GetFromJsonAsync<CartDto>($"/api/v1/customers/{customerId}/cart", JsonOptions);
			cart!.Items.Should().ContainSingle(i => i.ProductId == productId);
		}
		finally
		{
			await using var scope = _fixture.Services.CreateAsyncScope();
			var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
			var product = await db.Product.SingleAsync(p => (int)p.Id == productId);
			product.ChangePrice(originalPrice);
			await db.SaveChangesAsync();
			await ClearCartAsync(customerId);
		}
	}

	[Fact]
	public async Task Checkout_idempotent_replay_does_not_duplicate_order()
	{
		const int customerId = 7;
		await ClearCartAsync(customerId);
		(await _http.PostAsJsonAsync(
			$"/api/v1/customers/{customerId}/cart/items",
			new { productId = 11, quantity = 1 })).EnsureSuccessStatusCode();

		var key = Ulid.NewUlid().ToString();
		async Task<HttpResponseMessage> SendAsync()
		{
			var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/customers/{customerId}/checkout");
			req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
			req.Content = JsonContent.Create(DefaultAddress());
			return await _http.SendAsync(req);
		}

		using var first = await SendAsync();
		first.StatusCode.Should().Be(HttpStatusCode.OK);
		var firstBody = await first.Content.ReadFromJsonAsync<CheckoutDto>(JsonOptions);

		// Cart cleared — replay must return cached response without second create.
		using var second = await SendAsync();
		second.StatusCode.Should().Be(HttpStatusCode.OK);
		var secondBody = await second.Content.ReadFromJsonAsync<CheckoutDto>(JsonOptions);
		secondBody!.OrderId.Should().Be(firstBody!.OrderId);
		secondBody.DomainOrderId.Should().Be(firstBody.DomainOrderId);

		await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, firstBody.DomainOrderId);
	}

	[Fact]
	public async Task CreateOrder_duplicate_lines_merge_and_decrement_stock_once()
	{
		using var capture = new InProcessActivityCapture();
		int stockBefore;
		await using (var scope = _fixture.Services.CreateAsyncScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
			stockBefore = (await db.Product.AsNoTracking().SingleAsync(p => (int)p.Id == 10)).StockQuantity;
		}

		var key = Ulid.NewUlid().ToString();
		using var request = new HttpRequestMessage(HttpMethod.Post, HttpOrderCreate.Path);
		request.Headers.TryAddWithoutValidation(HttpOrderCreate.IdempotencyHeader, key);
		request.Content = new StringContent(
			"""{"customerId":8,"items":[{"productId":10,"quantity":1},{"productId":10,"quantity":2}]}""",
			Encoding.UTF8,
			"application/json");

		using var response = await _http.SendAsync(request);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>(JsonOptions);
		body!.Quantity.Should().Be(3);

		await using (var scope = _fixture.Services.CreateAsyncScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
			var order = await db.Orders.AsNoTracking().Include(o => o.Items)
				.SingleAsync(o => (int)o.Id == body.DomainOrderId);
			order.Items.Should().ContainSingle();
			order.Items.Single().Quantity.Should().Be(3);
			var stockAfter = (await db.Product.AsNoTracking().SingleAsync(p => (int)p.Id == 10)).StockQuantity;
			stockAfter.Should().Be(stockBefore - 3);
		}

		await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, body.DomainOrderId);
	}

	[Fact]
	public async Task Concurrent_create_on_low_stock_never_goes_negative()
	{
		// Use LOW-002 stock=1 (product id 18 in seed order: 1..19 flagships, LOW-002 is 18)
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var low = (await db.Product.AsNoTracking().ToListAsync())
			.Single(p => p.Sku.Value == "SKU-LOW-002");
		var productId = low.Id.Value;
		low.StockQuantity.Should().Be(1);

		using var capture = new InProcessActivityCapture();
		var tasks = Enumerable.Range(0, 4).Select(async i =>
		{
			var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
			return await HttpOrderCreate.PostAsync(
				client, capture, Ulid.NewUlid().ToString(), quantity: 1, productId: productId, customerId: 9);
		}).ToArray();

		var results = await Task.WhenAll(tasks);
		results.Count(r => r.HttpStatus == 200).Should().Be(1);
		results.Count(r => r.HttpStatus == 409).Should().Be(3);

		var stock = (await db.Product.AsNoTracking().SingleAsync(p => (int)p.Id == productId)).StockQuantity;
		stock.Should().Be(0);

		foreach (var ok in results.Where(r => r.HttpStatus == 200))
		{
			var created = JsonSerializer.Deserialize<CreateOrderResponse>(ok.Body, JsonOptions);
			if (created is not null)
				await CreatedOrderCleanup.DeleteByDomainIdsAsync(_fixture.Services, created.DomainOrderId);
		}

		// Restore low-stock fixture for other suite tests.
		await db.Database.ExecuteSqlRawAsync(
			"""UPDATE products SET "StockQuantity" = 1 WHERE "Id" = {0}""",
			productId);
	}

	private async Task ClearCartAsync(int customerId)
	{
		await _http.DeleteAsync($"/api/v1/customers/{customerId}/cart");
	}

	private async Task<int> CountOrdersAsync()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		return await db.Orders.CountAsync();
	}

	private static object DefaultAddress() => new
	{
		recipientName = "Test User",
		line1 = "2 Test Ave",
		city = "Munich",
		postalCode = "80331",
		country = "DE"
	};

	private sealed record CartDto(int CustomerId, List<CartItemDto> Items);
	private sealed record CartItemDto(int ProductId, int Quantity);
	private sealed record CheckoutDto(
		Guid OrderId,
		int DomainOrderId,
		string OrderNumber,
		string Status,
		decimal Subtotal,
		decimal TaxAmount,
		decimal ShippingAmount,
		decimal GrandTotal,
		string Currency,
		string PaymentDecision,
		DateTime OrderDate);
	private sealed record CreateOrderResponse(
		Guid OrderId,
		int DomainOrderId,
		string OrderNumber,
		string Status,
		int Quantity,
		decimal TotalAmount);
}
