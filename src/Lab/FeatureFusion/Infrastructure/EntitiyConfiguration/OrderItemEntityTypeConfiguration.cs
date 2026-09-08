using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class OrderItemEntityTypeConfiguration : IEntityTypeConfiguration<OrderItem>
{
	public void Configure(EntityTypeBuilder<OrderItem> builder)
	{
		builder.ToTable("order_items");
		builder.HasKey(i => i.Id);
		builder.Ignore(i => i.LineTotal);

		builder.Property(i => i.Id)
			.HasIdentityConversion<OrderItemId, int>(v => new OrderItemId(v))
			.ValueGeneratedOnAdd();

		builder.Property(i => i.OrderId)
			.HasIdentityConversion<OrderId, int>(v => new OrderId(v));

		builder.Property(i => i.ProductId)
			.HasIdentityConversion<ProductId, int>(v => new ProductId(v));

		builder.Property(i => i.UnitPrice).HasPrecision(18, 2);

		builder.HasOne(i => i.Product)
			.WithMany()
			.HasForeignKey(i => i.ProductId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasIndex(i => i.OrderId).HasDatabaseName("IX_order_items_order_id");
		builder.HasIndex(i => i.ProductId).HasDatabaseName("IX_order_items_product_id");
	}
}
