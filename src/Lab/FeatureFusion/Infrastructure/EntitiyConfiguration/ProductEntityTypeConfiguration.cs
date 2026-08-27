using FeatureFusion.Domain.Entities;
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
	}
}
