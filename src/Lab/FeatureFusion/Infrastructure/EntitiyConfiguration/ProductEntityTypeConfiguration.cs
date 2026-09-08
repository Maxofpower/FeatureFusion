using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class ProductEntityTypeConfiguration : IEntityTypeConfiguration<Product>
{
	public void Configure(EntityTypeBuilder<Product> builder)
	{
		builder.ToTable("products");
		builder.HasKey(p => p.Id);
		builder.Ignore(p => p.DomainEvents);
		// AggregateRoot.OriginalVersion is the optimistic concurrency token (IHaveAggregateVersion).
		builder.Property(p => p.OriginalVersion).IsConcurrencyToken();

		builder.Property(p => p.Id)
			.HasIdentityConversion<ProductId, int>(v => new ProductId(v))
			.ValueGeneratedOnAdd();

		builder.Property(p => p.Name)
			.IsRequired()
			.HasMaxLength(256);

		builder.Property(p => p.Sku)
			.HasValueObjectConversion(s => s.Value, v => Sku.Create(v))
			.HasMaxLength(64)
			.IsRequired();

		builder.Property(p => p.Slug)
			.HasValueObjectConversion(s => s.Value, v => Slug.Create(v))
			.HasMaxLength(128)
			.IsRequired();

		builder.Property(p => p.ShortDescription).HasMaxLength(512);

		builder.Property(p => p.FullDescription);

		builder.Property(p => p.Price)
			.HasPrecision(18, 2);

		builder.Property(p => p.BrandId)
			.HasIdentityConversion<BrandId, int>(v => new BrandId(v))
			.IsRequired();

		builder.Property(p => p.CategoryId)
			.HasIdentityConversion<CategoryId, int>(v => new CategoryId(v))
			.IsRequired();

		builder.Property(p => p.CreatedAt)
			.HasConversion(
				v => v,
				v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

		builder.HasOne(p => p.Brand)
			.WithMany()
			.HasForeignKey(p => p.BrandId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasOne(p => p.Category)
			.WithMany()
			.HasForeignKey(p => p.CategoryId)
			.OnDelete(DeleteBehavior.Restrict);

		builder.HasIndex(p => p.Sku)
			.IsUnique()
			.HasDatabaseName("IX_products_sku");

		builder.HasIndex(p => p.Slug)
			.IsUnique()
			.HasDatabaseName("IX_products_slug");

		builder.HasIndex(p => p.BrandId)
			.HasDatabaseName("IX_products_brand_id");

		builder.HasIndex(p => p.CategoryId)
			.HasDatabaseName("IX_products_category_id");

		builder.HasIndex(p => p.Name)
			.HasDatabaseName("IX_products_name");

		builder.HasIndex(p => p.CreatedAt)
			.IsDescending(false)
			.HasDatabaseName("IX_products_created_at_asc");

		builder.HasIndex(p => p.CreatedAt)
			.IsDescending(true)
			.HasDatabaseName("IX_products_created_at_desc");

		builder.HasIndex(["Price", "Id"], "IX_Product_Price_Id_AA")
			.HasDatabaseName("IX_products_price_id");
		builder.HasIndex(["Price", "Id"], "IX_Product_Price_Id_DA")
			.IsDescending(true, false)
			.HasDatabaseName("IX_products_price_id_desc");
		builder.HasIndex(["CreatedAt", "Id"], "IX_Product_CreatedAt_Id_AA")
			.HasDatabaseName("IX_products_created_at_id");
		builder.HasIndex(["CreatedAt", "Id"], "IX_Product_CreatedAt_Id_DA")
			.IsDescending(true, false)
			.HasDatabaseName("IX_products_created_at_id_desc");
		builder.HasIndex(["Name", "Id"], "IX_Product_Name_Id_AA")
			.HasDatabaseName("IX_products_name_id");
		builder.HasIndex(["Name", "Id"], "IX_Product_Name_Id_DA")
			.IsDescending(true, false)
			.HasDatabaseName("IX_products_name_id_desc");
		builder.HasIndex(["Name", "Price", "Id"], "IX_Product_Name_Price_Id_AAA")
			.HasDatabaseName("IX_products_name_price_id");
		builder.HasIndex(["Name", "Price", "Id"], "IX_Product_Name_Price_Id_DDD")
			.IsDescending()
			.HasDatabaseName("IX_products_name_price_id_desc");

		builder.HasMany(p => p.Images)
			.WithOne()
			.HasForeignKey(i => i.ProductId)
			.OnDelete(DeleteBehavior.Cascade);
		builder.Navigation(p => p.Images)
			.HasField("_images")
			.UsePropertyAccessMode(PropertyAccessMode.Field);

		builder.HasMany(p => p.Specifications)
			.WithOne()
			.HasForeignKey(s => s.ProductId)
			.OnDelete(DeleteBehavior.Cascade);
		builder.Navigation(p => p.Specifications)
			.HasField("_specifications")
			.UsePropertyAccessMode(PropertyAccessMode.Field);
	}
}
