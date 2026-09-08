using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Customers;
using FeatureFusion.Domain.Orders;
using FluentAssertions;
using BuildingBlocks.Domain;
using OrderEntity = FeatureFusion.Domain.Orders.Order;

namespace IntegrationTests.DemoCommerce;

/// <summary>Domain-level order invariants and lifecycle transitions.</summary>
public sealed class OrderAggregateTests
{
	[Fact]
	public void Create_requires_at_least_one_line()
	{
		var act = () => OrderEntity.Create(
			OrderNumber.Create("ORD-TEST-1"),
			CustomerId.From(1),
			OrderStatus.Placed,
			"EUR",
			DateTime.UtcNow,
			[]);

		act.Should().Throw<DomainException>().WithMessage("*one line*");
	}

	[Fact]
	public void Create_rejects_non_positive_quantity()
	{
		var act = () => OrderEntity.Create(
			OrderNumber.Create("ORD-TEST-2"),
			CustomerId.From(1),
			OrderStatus.Placed,
			"EUR",
			DateTime.UtcNow,
			[(ProductId.From(1), 0, 10m, null)]);

		act.Should().Throw<DomainException>();
	}

	[Fact]
	public void Create_computes_total_from_line_snapshots_and_breakdown()
	{
		var order = OrderEntity.Create(
			OrderNumber.Create("ORD-TEST-3"),
			CustomerId.From(1),
			OrderStatus.Placed,
			"EUR",
			DateTime.UtcNow,
			[
				(ProductId.From(1), 2, 10.50m, null),
				(ProductId.From(2), 1, 5m, null)
			],
			taxAmount: 2.60m,
			shippingAmount: 4.99m);

		order.Subtotal.Should().Be(26.00m);
		order.TaxAmount.Should().Be(2.60m);
		order.ShippingAmount.Should().Be(4.99m);
		order.Total.Should().Be(33.59m);
		order.Items.Should().HaveCount(2);
		order.Status.Should().Be(OrderStatus.Placed);
	}

	[Fact]
	public void Product_CanFulfill_and_TryDecrementStock()
	{
		var product = Product.Create(
			"Test Phone",
			Sku.Create("SKU-TEST-1"),
			price: 100m,
			stockQuantity: 2,
			BrandId.From(1),
			CategoryId.From(1),
			DateTime.UtcNow);

		product.CanFulfill(2).Should().BeTrue();
		product.TryDecrementStock(2).Should().BeTrue();
		product.StockQuantity.Should().Be(0);
		product.TryDecrementStock(1).Should().BeFalse();
		product.StockQuantity.Should().Be(0);

		product.ChangePrice(150m);
		product.Price.Should().Be(150m);
	}

	[Fact]
	public void Status_transitions_are_controlled()
	{
		var pending = OrderEntity.Create(
			OrderNumber.Create("ORD-TEST-4"),
			CustomerId.From(1),
			OrderStatus.Pending,
			"EUR",
			DateTime.UtcNow,
			[(ProductId.From(1), 1, 10m, null)]);

		pending.MarkPlaced();
		pending.Status.Should().Be(OrderStatus.Placed);
		pending.Cancel();
		pending.Status.Should().Be(OrderStatus.Cancelled);

		var pendingFail = OrderEntity.Create(
			OrderNumber.Create("ORD-TEST-5"),
			CustomerId.From(1),
			OrderStatus.Pending,
			"EUR",
			DateTime.UtcNow,
			[(ProductId.From(1), 1, 10m, null)]);
		pendingFail.MarkPaymentFailed();
		pendingFail.Status.Should().Be(OrderStatus.PaymentFailed);

		var placed = OrderEntity.Create(
			OrderNumber.Create("ORD-TEST-6"),
			CustomerId.From(1),
			OrderStatus.Placed,
			"EUR",
			DateTime.UtcNow,
			[(ProductId.From(1), 1, 10m, null)]);
		var bad = () => placed.MarkPaymentFailed();
		bad.Should().Throw<DomainException>();
	}
}
