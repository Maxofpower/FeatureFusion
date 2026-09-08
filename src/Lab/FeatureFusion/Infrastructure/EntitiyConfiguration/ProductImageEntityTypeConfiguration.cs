using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class ProductImageEntityTypeConfiguration : IEntityTypeConfiguration<ProductImage>
{
	public void Configure(EntityTypeBuilder<ProductImage> builder)
	{
		builder.ToTable("product_images");
		builder.HasKey(i => i.Id);
		builder.Property(i => i.Id)
			.HasIdentityConversion<ProductImageId, int>(v => new ProductImageId(v))
			.ValueGeneratedOnAdd();
		builder.Property(i => i.ProductId)
			.HasIdentityConversion<ProductId, int>(v => new ProductId(v));
		builder.Property(i => i.Url).IsRequired().HasMaxLength(512);
		builder.Property(i => i.AltText).IsRequired().HasMaxLength(256);
		builder.HasIndex(i => i.ProductId).HasDatabaseName("IX_product_images_product_id");
	}
}
