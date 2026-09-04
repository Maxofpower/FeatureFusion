using BuildingBlocks.Pagination.EntityFrameworkCore;
using FeatureFusion.Domain.Entities;
using FeatureFusion.Infrastructure.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

class ProductEntityTypeConfiguration
	: IEntityTypeConfiguration<Product>
{
	public void Configure(EntityTypeBuilder<Product> builder)
	{
		builder.ToTable("products");
		builder.HasKey(p => p.Id);

		builder.Property(p => p.Id)
			   .ValueGeneratedOnAdd(); 

		builder.Property(ci => ci.Name);

		builder.Property(p => p.CreatedAt)
		 .HasConversion(
			 v => v,                         
			 v => DateTime.SpecifyKind(v, DateTimeKind.Utc) 
		 );

		builder.HasIndex(ci => ci.Name)
			.HasDatabaseName("IX_products_name"); ;


		builder.HasIndex(ci => ci.CreatedAt)
			.IsDescending(false)
			.HasDatabaseName("IX_products_created_at_asc");

		builder.HasIndex(ci => ci.CreatedAt)
			.IsDescending(true)
			.HasDatabaseName("IX_products_created_at_desc");

		builder.HasKeysetIndex(ProductSortKeys.PriceAsc)
			.HasDatabaseName("IX_products_price_id");
		builder.HasKeysetIndex(ProductSortKeys.PriceDesc)
			.HasDatabaseName("IX_products_price_id_desc");
		builder.HasKeysetIndex(ProductSortKeys.CreatedAtAsc)
			.HasDatabaseName("IX_products_created_at_id");
		builder.HasKeysetIndex(ProductSortKeys.CreatedAtDesc)
			.HasDatabaseName("IX_products_created_at_id_desc");
		builder.HasKeysetIndex(ProductSortKeys.NameAsc)
			.HasDatabaseName("IX_products_name_id");
		builder.HasKeysetIndex(ProductSortKeys.NameDesc)
			.HasDatabaseName("IX_products_name_id_desc");
		builder.HasKeysetIndex(ProductSortKeys.NameThenPriceAsc)
			.HasDatabaseName("IX_products_name_price_id");
		builder.HasKeysetIndex(ProductSortKeys.NameThenPriceDesc)
			.HasDatabaseName("IX_products_name_price_id_desc");
	}
}
