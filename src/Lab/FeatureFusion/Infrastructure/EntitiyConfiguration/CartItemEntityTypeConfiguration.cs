using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Carts;
using FeatureFusion.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class CartItemEntityTypeConfiguration : IEntityTypeConfiguration<CartItem>
{
	public void Configure(EntityTypeBuilder<CartItem> builder)
	{
		builder.ToTable("cart_items");
		builder.HasKey(i => i.Id);

		builder.Property(i => i.Id)
			.HasIdentityConversion<CartItemId, int>(v => new CartItemId(v))
			.ValueGeneratedOnAdd();

		builder.Property(i => i.CartId)
			.HasIdentityConversion<CartId, int>(v => new CartId(v));

		builder.Property(i => i.ProductId)
			.HasIdentityConversion<ProductId, int>(v => new ProductId(v));

		builder.HasIndex(i => new { i.CartId, i.ProductId })
			.IsUnique()
			.HasDatabaseName("IX_cart_items_cart_product");
	}
}
