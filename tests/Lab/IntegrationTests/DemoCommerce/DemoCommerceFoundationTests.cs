using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Customers;
using FeatureFusion.Domain.Orders;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.Seeding;
using FluentAssertions;
using IntegrationTests.Aspire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.DemoCommerce;

/// <summary>
/// Seed and schema shape for the storefront catalog (listing + detail) plus lab orders.
/// Does not change CreateOrder, MCP, Admission, or pagination experiment contracts.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class DemoCommerceFoundationTests
{
	private readonly AspireFixture _fixture;

	public DemoCommerceFoundationTests(AspireFixture fixture) => _fixture = fixture;

	[Fact]
	public async Task Seed_counts_match_expected_fixtures()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		(await db.Brands.CountAsync()).Should().Be(DemoCommerceSeed.ExpectedBrandCount);
		(await db.Categories.CountAsync()).Should().Be(DemoCommerceSeed.ExpectedCategoryCount);
		(await db.Product.CountAsync()).Should().Be(DemoCommerceSeed.ExpectedProductCount);
		(await db.Customers.CountAsync()).Should().Be(DemoCommerceSeed.ExpectedCustomerCount);

		// CreateOrder (HTTP/MCP/Exp) inserts real Orders into the shared Aspire DB.
		// Assert seed fixtures by order-number convention, not global COUNT(*).
		var numbers = await db.Orders.AsNoTracking().Select(o => o.OrderNumber.Value).ToListAsync();
		numbers.Count(DemoCommerceSeed.IsSeedOrderNumber).Should().Be(DemoCommerceSeed.ExpectedOrderCount);
	}

	[Fact]
	public async Task Products_link_to_brands_and_categories()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		var sample = await db.Product
			.Include(p => p.Brand)
			.Include(p => p.Category)
			.Include(p => p.Images)
			.Include(p => p.Specifications)
			.Where(p => p.Sku == Sku.Create(DemoCommerceSeed.FlagshipSku))
			.SingleAsync();

		sample.Brand!.Name.Should().Be("Apple");
		sample.Brand.Slug.Value.Should().Be(DemoCommerceSeed.FlagshipBrandSlug);
		sample.Category!.Name.Should().Be("Smartphones");
		sample.Slug.Value.Should().Be(DemoCommerceSeed.FlagshipSlug);
		sample.StockQuantity.Should().BeGreaterThan(0);
		sample.Images.Should().HaveCountGreaterThanOrEqualTo(3);
		sample.Images.Should().ContainSingle(i => i.IsPrimary);
		sample.Specifications.Should().HaveCountGreaterThanOrEqualTo(3);
	}

	[Fact]
	public async Task Every_product_has_listing_fields_and_a_primary_image()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		(await db.Product.CountAsync(p => !p.Images.Any(i => i.IsPrimary)))
			.Should().Be(0);
		(await db.Product.SelectMany(p => p.Images).CountAsync())
			.Should().BeGreaterThanOrEqualTo(DemoCommerceSeed.ExpectedProductCount);
	}

	[Fact]
	public async Task Crafted_stock_and_pagination_fixtures_exist()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		var oos = await db.Product.SingleAsync(p => p.Sku == Sku.Create(DemoCommerceSeed.OutOfStockSku));
		oos.StockQuantity.Should().Be(0);

		(await db.Product.CountAsync(p => p.StockQuantity > 0 && p.StockQuantity <= 3))
			.Should().BeGreaterThanOrEqualTo(3);

		(await db.Product.CountAsync(p => p.Price == DemoCommerceSeed.DuplicatePrice))
			.Should().Be(20);

		(await db.Product.CountAsync(p => p.CreatedAt == DemoCommerceSeed.DuplicateCreatedAt))
			.Should().BeGreaterThanOrEqualTo(12);

		var decline = (await db.Product.AsNoTracking().ToListAsync())
			.Single(p => p.Sku.Value == DemoCommerceSeed.PaymentDeclineSku);
		decline.Price.Should().Be(DemoCommerceSeed.PaymentDeclinePrice);
	}

	[Fact]
	public async Task Orders_belong_to_customers_with_line_items()
	{
		await using var scope = _fixture.Services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		var power = await db.Customers
			.SingleAsync(c => c.Email == Email.Create(DemoCommerceSeed.PowerCustomerEmail));
		power.DisplayName.Should().Be("Alex Power");

		var powerOrders = (await db.Orders
				.Include(o => o.Items)
				.Where(o => o.CustomerId == power.Id)
				.ToListAsync())
			.Where(o => DemoCommerceSeed.IsSeedOrderNumber(o.OrderNumber.Value))
			.ToList();
		powerOrders.Should().HaveCount(6);
		powerOrders.Should().OnlyContain(o =>
			o.OrderNumber.Value.StartsWith("ORD-PWR-", StringComparison.Ordinal)
			&& o.Items.Count >= 2);

		var high = await db.Orders
			.Include(o => o.Items)
			.SingleAsync(o => o.OrderNumber == OrderNumber.Create(DemoCommerceSeed.HighValueOrderNumber));
		high.Total.Should().Be(high.Items.Sum(i => i.Quantity * i.UnitPrice));
		high.Total.Should().BeGreaterThan(5000m);
		high.Status.Should().Be(OrderStatus.Pending);
	}
}
