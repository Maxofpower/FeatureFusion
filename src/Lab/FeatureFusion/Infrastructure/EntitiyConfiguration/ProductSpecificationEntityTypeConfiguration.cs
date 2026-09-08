using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class ProductSpecificationEntityTypeConfiguration : IEntityTypeConfiguration<ProductSpecification>
{
	public void Configure(EntityTypeBuilder<ProductSpecification> builder)
	{
		builder.ToTable("product_specifications");
		builder.HasKey(s => s.Id);
		builder.Property(s => s.Id)
			.HasIdentityConversion<ProductSpecificationId, int>(v => new ProductSpecificationId(v))
			.ValueGeneratedOnAdd();
		builder.Property(s => s.ProductId)
			.HasIdentityConversion<ProductId, int>(v => new ProductId(v));
		builder.Property(s => s.Name).IsRequired().HasMaxLength(128);
		builder.Property(s => s.Value).IsRequired().HasMaxLength(256);
		builder.HasIndex(s => s.ProductId).HasDatabaseName("IX_product_specifications_product_id");
	}
}
