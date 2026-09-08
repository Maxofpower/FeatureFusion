using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Customers;
using FeatureFusion.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class OrderEntityTypeConfiguration : IEntityTypeConfiguration<Order>
{
	public void Configure(EntityTypeBuilder<Order> builder)
	{
		builder.ToTable("orders");
		builder.HasKey(o => o.Id);
		builder.Ignore(o => o.DomainEvents);
		builder.Ignore(o => o.OriginalVersion);

		builder.Property(o => o.Id)
			.HasIdentityConversion<OrderId, int>(v => new OrderId(v))
			.ValueGeneratedOnAdd();

		builder.Property(o => o.OrderNumber)
			.HasValueObjectConversion(n => n.Value, v => OrderNumber.Create(v))
			.IsRequired()
			.HasMaxLength(32);
		builder.Property(o => o.Currency).IsRequired().HasMaxLength(3);
		builder.Property(o => o.Subtotal).HasPrecision(18, 2);
		builder.Property(o => o.TaxAmount).HasPrecision(18, 2);
		builder.Property(o => o.ShippingAmount).HasPrecision(18, 2);
		builder.Property(o => o.Total).HasPrecision(18, 2);
		builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
		builder.Property(o => o.CreatedAt)
			.HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

		builder.OwnsOne(o => o.Shipping, ship =>
		{
			ship.Property(s => s.RecipientName).HasColumnName("shipping_recipient").HasMaxLength(128);
			ship.Property(s => s.Line1).HasColumnName("shipping_line1").HasMaxLength(256);
			ship.Property(s => s.City).HasColumnName("shipping_city").HasMaxLength(128);
			ship.Property(s => s.PostalCode).HasColumnName("shipping_postal").HasMaxLength(32);
			ship.Property(s => s.Country).HasColumnName("shipping_country").HasMaxLength(2);
			ship.Property(s => s.Method).HasColumnName("shipping_method").HasMaxLength(64);
			ship.Property(s => s.Status).HasColumnName("shipping_status").HasConversion<string>().HasMaxLength(32);
		});
		builder.Navigation(o => o.Shipping).IsRequired();

		builder.Property(o => o.CustomerId)
			.HasIdentityConversion<CustomerId, int>(v => new CustomerId(v));

		builder.HasOne(o => o.Customer)
			.WithMany()
			.HasForeignKey(o => o.CustomerId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasMany(o => o.Items)
			.WithOne(i => i.Order)
			.HasForeignKey(i => i.OrderId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.Navigation(o => o.Items)
			.HasField("_items")
			.UsePropertyAccessMode(PropertyAccessMode.Field);

		builder.HasIndex(o => o.OrderNumber).IsUnique();
		builder.HasIndex(o => o.CustomerId).HasDatabaseName("IX_orders_customer_id");
		builder.HasIndex(o => o.Status).HasDatabaseName("IX_orders_status");
		builder.HasIndex(o => o.CreatedAt).HasDatabaseName("IX_orders_created_at");
	}
}
