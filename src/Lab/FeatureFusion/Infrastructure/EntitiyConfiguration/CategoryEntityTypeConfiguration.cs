using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class CategoryEntityTypeConfiguration : IEntityTypeConfiguration<Category>
{
	public void Configure(EntityTypeBuilder<Category> builder)
	{
		builder.ToTable("categories");
		builder.HasKey(c => c.Id);
		builder.Property(c => c.Id)
			.HasIdentityConversion<CategoryId, int>(v => new CategoryId(v))
			.ValueGeneratedOnAdd();
		builder.Property(c => c.Name).IsRequired().HasMaxLength(128);
		builder.Property(c => c.Slug)
			.HasValueObjectConversion(s => s.Value, v => Slug.Create(v))
			.HasMaxLength(128)
			.IsRequired();
		builder.HasIndex(c => c.Name).IsUnique();
		builder.HasIndex(c => c.Slug).IsUnique();
	}
}
