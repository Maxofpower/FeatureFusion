using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Carts;
using FeatureFusion.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class CartEntityTypeConfiguration : IEntityTypeConfiguration<Cart>
{
	public void Configure(EntityTypeBuilder<Cart> builder)
	{
		builder.ToTable("carts");
		builder.HasKey(c => c.Id);
		builder.Ignore(c => c.DomainEvents);
		builder.Ignore(c => c.OriginalVersion);

		builder.Property(c => c.Id)
			.HasIdentityConversion<CartId, int>(v => new CartId(v))
			.ValueGeneratedOnAdd();

		builder.Property(c => c.CustomerId)
			.HasIdentityConversion<CustomerId, int>(v => new CustomerId(v))
			.IsRequired();

		builder.Property(c => c.UpdatedAtUtc)
			.HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

		builder.HasIndex(c => c.CustomerId)
			.IsUnique()
			.HasDatabaseName("IX_carts_customer_id");

		builder.HasMany(c => c.Items)
			.WithOne(i => i.Cart)
			.HasForeignKey(i => i.CartId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.Navigation(c => c.Items)
			.HasField("_items")
			.UsePropertyAccessMode(PropertyAccessMode.Field);
	}
}
