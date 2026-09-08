using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Customers;
using FeatureFusion.Domain.Orders;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FeatureFusion.Infrastructure.Seeding;

/// <summary>
/// Deterministic storefront seed: catalog (listing + detail), customers, and orders.
/// Keeps 1000 products so existing pagination experiments remain valid.
/// Does not emit outbox or integration events.
/// </summary>
public static class DemoCommerceSeed
{
	public const int ExpectedBrandCount = 9;
	public const int ExpectedCategoryCount = 7;
	public const int ExpectedProductCount = 1000;
	public const int ExpectedCustomerCount = 25;
	public const int ExpectedOrderCount = 50;
	/// <summary>Sellable catalog stock pool so Exp CreateOrder storms cannot exhaust SKUs.</summary>
	public const int SellableStockPool = 100_000;
	public const decimal DuplicatePrice = 199.00m;
	public static readonly DateTime DuplicateCreatedAt = new(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
	public const string HighValueOrderNumber = "ORD-HIGH-001";
	public const string OutOfStockSku = "SKU-OOS-001";
	/// <summary>
	/// Qty 1 checkout of this SKU yields grand total ending in .13 (DemoPaymentProcessor decline).
	/// Formula: round(4.67 × 1.10, 2) + 4.99 shipping = 10.13.
	/// </summary>
	public const string PaymentDeclineSku = "SKU-PAY-013";
	public const decimal PaymentDeclinePrice = 4.67m;
	public const string PowerCustomerEmail = "alex.power@example.com";
	public const string FlagshipSlug = "iphone-15-pro";
	public const string FlagshipSku = "SKU-APL-IP15";
	public const string FlagshipBrandSlug = "apple";
	public const string FlagshipCategorySlug = "smartphones";

	/// <summary>
	/// Seed fixtures use <c>ORD-PWR-*</c>, <c>ORD-HIGH-001</c>, or <c>ORD-####</c>.
	/// Runtime CreateOrder uses <c>ORD-{Ulid}</c> and must not be counted as seed.
	/// </summary>
	public static bool IsSeedOrderNumber(string? orderNumber)
	{
		if (string.IsNullOrEmpty(orderNumber) || !orderNumber.StartsWith("ORD-", StringComparison.Ordinal))
			return false;
		if (orderNumber.StartsWith("ORD-PWR-", StringComparison.Ordinal))
			return true;
		if (orderNumber == HighValueOrderNumber)
			return true;
		return orderNumber.Length == 8
			&& char.IsDigit(orderNumber[4])
			&& char.IsDigit(orderNumber[5])
			&& char.IsDigit(orderNumber[6])
			&& char.IsDigit(orderNumber[7]);
	}

	private static readonly string[] BrandNames =
	[
		"Apple", "Samsung", "Google", "Sony", "Dell", "Lenovo", "Bose", "Logitech", "ASUS"
	];

	private static readonly string[] CategoryNames =
	[
		"Smartphones", "Laptops", "Tablets", "Audio", "Accessories", "Monitors", "Wearables"
	];

	public static async Task SeedAsync(CatalogDbContext context, ILogger logger, CancellationToken cancellationToken = default)
	{
		if (await context.Product.AnyAsync(cancellationToken).ConfigureAwait(false))
		{
			logger.LogInformation("Demo Commerce seed skipped — catalog already present.");
			return;
		}

		if (await context.Brands.AnyAsync(cancellationToken).ConfigureAwait(false))
		{
			context.Brands.RemoveRange(context.Brands);
			context.Categories.RemoveRange(context.Categories);
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		var brands = BrandNames.Select((name, i) =>
		{
			var slug = Slug.FromName(name);
			return Brand.Create(
				name,
				slug,
				logoUrl: $"/media/brands/{slug.Value}.webp",
				id: new BrandId(i + 1));
		}).ToList();

		var categories = CategoryNames.Select((name, i) =>
			Category.Create(name, Slug.FromName(name), new CategoryId(i + 1))).ToList();

		await context.Brands.AddRangeAsync(brands, cancellationToken).ConfigureAwait(false);
		await context.Categories.AddRangeAsync(categories, cancellationToken).ConfigureAwait(false);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var products = BuildProducts(brands, categories);
		await context.Product.AddRangeAsync(products, cancellationToken).ConfigureAwait(false);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var customers = BuildCustomers();
		await context.Customers.AddRangeAsync(customers, cancellationToken).ConfigureAwait(false);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		var orders = BuildOrders(customers, products);
		await context.Orders.AddRangeAsync(orders, cancellationToken).ConfigureAwait(false);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await ResetIdentityAsync(context, "brands", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "categories", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "products", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "product_images", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "product_specifications", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "customers", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "orders", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "order_items", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "carts", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "cart_items", cancellationToken).ConfigureAwait(false);
		await ResetIdentityAsync(context, "payment_records", cancellationToken).ConfigureAwait(false);

		logger.LogInformation(
			"Demo Commerce seeded: {Brands} brands, {Categories} categories, {Products} products, {Customers} customers, {Orders} orders.",
			brands.Count, categories.Count, products.Count, customers.Count, orders.Count);
	}

	private static async Task ResetIdentityAsync(CatalogDbContext context, string table, CancellationToken cancellationToken)
	{
		// Table names are fixed seed identifiers, not user input.
#pragma warning disable EF1002
		await context.Database.ExecuteSqlRawAsync(
			$"SELECT setval(pg_get_serial_sequence('\"{table}\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"{table}\"), 1));",
			cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
	}

	private static List<Product> BuildProducts(List<Brand> brands, List<Category> categories)
	{
		var products = new List<Product>(ExpectedProductCount);
		var flagships = new (string Sku, string Name, string Brand, string Category, decimal Price, int Stock, string Short, string Full)[]
		{
			(FlagshipSku, "iPhone 15 Pro", "Apple", "Smartphones", 1199m, SellableStockPool,
				"Titanium smartphone with a pro camera system.",
				"Flagship smartphone for listing and detail pages: gallery, specifications, and related products in the same category."),
			("SKU-SAM-S24", "Galaxy S24 Ultra", "Samsung", "Smartphones", 1299m, SellableStockPool,
				"S Pen smartphone with a 200 MP camera.",
				"Android flagship used for brand and category filters on the listing page."),
			("SKU-GOO-PX8", "Pixel 8 Pro", "Google", "Smartphones", 999m, SellableStockPool,
				"Google Tensor phone with computational photography.",
				"Mid-high smartphone used as a related product on other smartphone detail pages."),
			("SKU-APL-MBP14", "MacBook Pro 14", "Apple", "Laptops", 1999m, SellableStockPool,
				"14-inch professional laptop.",
				"Laptop flagship for category filters and high-value order lines."),
			("SKU-DEL-XPS15", "XPS 15 OLED", "Dell", "Laptops", 1899m, SellableStockPool,
				"OLED creator laptop.",
				"Windows laptop flagship with a full gallery and specification table."),
			("SKU-LEN-X1", "ThinkPad X1 Carbon", "Lenovo", "Laptops", 1749m, SellableStockPool,
				"Ultralight business laptop.",
				"Business laptop used for listing cards and related-product chips."),
			("SKU-ASU-Z13", "Zenbook 14 OLED", "ASUS", "Laptops", 1299m, SellableStockPool,
				"Thin OLED ultrabook.",
				"Everyday laptop with stock depth for pagination volume around the flagships."),
			("SKU-APL-IPAD", "iPad Pro 12.9", "Apple", "Tablets", 1099m, SellableStockPool,
				"12.9-inch tablet for creative work.",
				"Tablet flagship for category browsing and detail-page related products."),
			("SKU-SAM-TAB", "Galaxy Tab S9", "Samsung", "Tablets", 899m, SellableStockPool,
				"Android tablet with an included stylus.",
				"Tablet used to keep the tablets category populated on listing pages."),
			("SKU-SON-WH1000", "WH-1000XM5", "Sony", "Audio", 399m, SellableStockPool,
				"Over-ear noise-cancelling headphones.",
				"Audio flagship for listing filters and out-of-stock contrast with Pixel Buds."),
			("SKU-BOS-QC45", "QuietComfort 45", "Bose", "Audio", 329m, SellableStockPool,
				"Comfort-focused wireless headphones.",
				"Audio product with a complete detail gallery."),
			("SKU-LOG-MX3", "MX Master 3S", "Logitech", "Accessories", 99m, SellableStockPool,
				"Precision wireless mouse.",
				"Accessory flagship used as a low-price listing card."),
			("SKU-DEL-U2723", "UltraSharp 27 USB-C", "Dell", "Monitors", 549m, SellableStockPool,
				"27-inch USB-C monitor.",
				"Monitor flagship for category filters and specification rows."),
			("SKU-APL-AW9", "Apple Watch Series 9", "Apple", "Wearables", 429m, SellableStockPool,
				"GPS smartwatch with a double tap gesture.",
				"Wearable flagship for listing chips and detail copy."),
			("SKU-SAM-GW6", "Galaxy Watch 6", "Samsung", "Wearables", 349m, SellableStockPool,
				"Wear OS smartwatch with body composition.",
				"Wearable used as a related product on other wearables."),
			(OutOfStockSku, "Pixel Buds Pro (Clearance)", "Google", "Audio", 189m, 0,
				"Clearance earbuds — currently out of stock.",
				"Out-of-stock fixture for listing availability badges."),
			("SKU-LOW-001", "Logitech Webcam C920e", "Logitech", "Accessories", 79m, 2,
				"1080p webcam with low remaining stock.",
				"Low-stock accessory fixture."),
			("SKU-LOW-002", "Sony WF-1000XM5", "Sony", "Audio", 279m, 1,
				"In-ear noise-cancelling earbuds.",
				"Low-stock audio fixture."),
			("SKU-LOW-003", "ASUS Portable Monitor", "ASUS", "Monitors", 249m, 3,
				"USB-C portable display.",
				"Low-stock monitor fixture."),
			(PaymentDeclineSku, "Demo cable (payment-decline fixture)", "Logitech", "Accessories", PaymentDeclinePrice, SellableStockPool,
				"Priced so a qty-1 checkout grand total ends in .13.",
				"Deterministic DemoPaymentProcessor decline fixture. Not a real SKU."),
		};

		var brandMap = brands.ToDictionary(b => b.Name, b => b);
		var categoryMap = categories.ToDictionary(c => c.Name, c => c);
		var id = 1;

		foreach (var f in flagships)
		{
			var brand = brandMap[f.Brand];
			var category = categoryMap[f.Category];
			var product = Product.Create(
				name: f.Name,
				sku: Sku.Create(f.Sku),
				price: f.Price,
				stockQuantity: f.Stock,
				brandId: brand.Id,
				categoryId: category.Id,
				createdAtUtc: DuplicateCreatedAt.AddDays(-(id % 20)),
				slug: Slug.FromName(f.Name),
				shortDescription: f.Short,
				fullDescription: f.Full,
				id: new ProductId(id++));
			AttachMedia(product, f.Brand, f.Category, flagship: true);
			products.Add(product);
		}

		for (var i = 0; i < 20; i++)
		{
			var brand = brands[i % brands.Count];
			var category = categories[i % categories.Count];
			var product = Product.Create(
				name: $"{brand.Name} Essentials {category.Name} {i + 1}",
				sku: Sku.Create($"SKU-DUP-PRICE-{i + 1:D2}"),
				price: DuplicatePrice,
				stockQuantity: SellableStockPool,
				brandId: brand.Id,
				categoryId: category.Id,
				createdAtUtc: DuplicateCreatedAt.AddHours(i),
				shortDescription: "Shared-price listing fixture.",
				fullDescription: "Shared-price pagination fixture.",
				id: new ProductId(id++));
			AttachMedia(product, brand.Name, category.Name, flagship: false);
			products.Add(product);
		}

		for (var i = 0; i < 12; i++)
		{
			var brand = brands[(i + 3) % brands.Count];
			var category = categories[(i + 2) % categories.Count];
			var product = Product.Create(
				name: $"{brand.Name} Studio Line {i + 1}",
				sku: Sku.Create($"SKU-DUP-DATE-{i + 1:D2}"),
				price: 149.50m + i,
				stockQuantity: SellableStockPool,
				brandId: brand.Id,
				categoryId: category.Id,
				createdAtUtc: DuplicateCreatedAt,
				shortDescription: "Shared created-at listing fixture.",
				fullDescription: "Shared-CreatedAt pagination fixture.",
				id: new ProductId(id++));
			AttachMedia(product, brand.Name, category.Name, flagship: false);
			products.Add(product);
		}

		while (products.Count < ExpectedProductCount)
		{
			var n = products.Count + 1;
			var brand = brands[n % brands.Count];
			var category = categories[n % categories.Count];
			var price = 49.99m + (n % 150) + (n % 7) * 0.13m;
			var product = Product.Create(
				name: $"{brand.Name} {category.Name} Model {n}",
				sku: Sku.Create($"SKU-CAT-{n:D4}"),
				price: Math.Round(price, 2),
				stockQuantity: SellableStockPool,
				brandId: brand.Id,
				categoryId: category.Id,
				createdAtUtc: DuplicateCreatedAt.AddDays(-(n % 400)).AddMinutes(n % 60),
				shortDescription: $"{category.Name} from {brand.Name}.",
				fullDescription: $"Catalog filler #{n} for pagination volume.",
				id: new ProductId(id++));
			AttachMedia(product, brand.Name, category.Name, flagship: false);
			products.Add(product);
		}

		return products;
	}

	private static void AttachMedia(Product product, string brandName, string categoryName, bool flagship)
	{
		var slug = product.Slug.Value;
		product.AddImage($"/media/catalog/{slug}-1.webp", product.Name, 0, isPrimary: true);
		if (flagship)
		{
			product.AddImage($"/media/catalog/{slug}-2.webp", $"{product.Name} — side", 1, isPrimary: false);
			product.AddImage($"/media/catalog/{slug}-3.webp", $"{product.Name} — detail", 2, isPrimary: false);
			product.AddSpecification("Brand", brandName, 0);
			product.AddSpecification("Category", categoryName, 1);
			product.AddSpecification("SKU", product.Sku.Value, 2);
			product.AddSpecification("Warranty", "24 months", 3);
			product.AddSpecification("Ships from", "EU warehouse", 4);
		}
		else
		{
			product.AddSpecification("Brand", brandName, 0);
			product.AddSpecification("Category", categoryName, 1);
		}
	}

	private static List<Customer> BuildCustomers()
	{
		var names = new[]
		{
			("Alex Power", PowerCustomerEmail),
			("Jordan Lee", "jordan.lee@example.com"),
			("Sam Rivera", "sam.rivera@example.com"),
			("Casey Nguyen", "casey.nguyen@example.com"),
			("Riley Patel", "riley.patel@example.com"),
			("Morgan Chen", "morgan.chen@example.com"),
			("Avery Brooks", "avery.brooks@example.com"),
			("Quinn Morales", "quinn.morales@example.com"),
			("Taylor Kim", "taylor.kim@example.com"),
			("Jamie Ortiz", "jamie.ortiz@example.com"),
			("Drew Hassan", "drew.hassan@example.com"),
			("Cameron Blake", "cameron.blake@example.com"),
			("Reese Alvarez", "reese.alvarez@example.com"),
			("Parker Singh", "parker.singh@example.com"),
			("Skyler Diaz", "skyler.diaz@example.com"),
			("Hayden Cole", "hayden.cole@example.com"),
			("Rowan West", "rowan.west@example.com"),
			("Emerson Shaw", "emerson.shaw@example.com"),
			("Finley Cross", "finley.cross@example.com"),
			("Kendall Frost", "kendall.frost@example.com"),
			("Peyton Vale", "peyton.vale@example.com"),
			("Blake Monroe", "blake.monroe@example.com"),
			("Charlie Dunn", "charlie.dunn@example.com"),
			("Dana Pierce", "dana.pierce@example.com"),
			("Elliot Nash", "elliot.nash@example.com"),
		};

		return names.Select((n, i) => Customer.Create(
			Email.Create(n.Item2),
			n.Item1,
			DuplicateCreatedAt.AddDays(-i * 3),
			new CustomerId(i + 1))).ToList();
	}

	private static List<Order> BuildOrders(List<Customer> customers, List<Product> products)
	{
		var orders = new List<Order>();
		var rng = new Random(42);
		var orderId = 1;
		var itemId = 1;
		var power = customers.First(c => c.Email.Value == PowerCustomerEmail);

		for (var i = 0; i < 6; i++)
		{
			orders.Add(MakeOrder(
				ref orderId, ref itemId, $"ORD-PWR-{i + 1:D3}", power, products, rng,
				i == 5 ? OrderStatus.Cancelled : OrderStatus.Placed,
				lineCount: 2 + (i % 3)));
		}

		var highProducts = products.Where(p => p.Price >= 900m && p.StockQuantity > 0).Take(5).ToList();
		var highLines = highProducts.Select(p =>
		{
			var qty = p.Price > 1500m ? 1 : 2;
			return (p.Id, qty, p.Price, (OrderItemId?)new OrderItemId(itemId++));
		}).ToList();

		orders.Add(Order.Create(
			OrderNumber.Create(HighValueOrderNumber),
			customers[1].Id,
			OrderStatus.Pending,
			"EUR",
			DuplicateCreatedAt.AddDays(-2),
			highLines,
			new OrderId(orderId++)));

		var otherCustomers = customers.Where(c => c.Id != power.Id).ToList();
		while (orders.Count < ExpectedOrderCount)
		{
			var customer = otherCustomers[rng.Next(otherCustomers.Count)];
			var status = (OrderStatus)(orders.Count % 3);
			orders.Add(MakeOrder(
				ref orderId, ref itemId, $"ORD-{orderId:D4}", customer, products, rng, status,
				lineCount: 2 + (orders.Count % 3)));
		}

		return orders;
	}

	private static Order MakeOrder(
		ref int orderId,
		ref int itemId,
		string number,
		Customer customer,
		List<Product> products,
		Random rng,
		OrderStatus status,
		int lineCount)
	{
		var id = new OrderId(orderId++);
		var sellable = products.Where(p => p.StockQuantity > 0 && p.Price > 0).ToList();
		var lines = new List<(ProductId, int, decimal, OrderItemId?)>();
		for (var i = 0; i < lineCount; i++)
		{
			var p = sellable[rng.Next(sellable.Count)];
			lines.Add((p.Id, 1 + rng.Next(3), p.Price, new OrderItemId(itemId++)));
		}

		return Order.Create(
			OrderNumber.Create(number),
			customer.Id,
			status,
			"EUR",
			DuplicateCreatedAt.AddDays(-(id.Value % 90)),
			lines,
			id);
	}
}
