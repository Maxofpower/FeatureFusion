using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class BrandEntityTypeConfiguration : IEntityTypeConfiguration<Brand>
{
	public void Configure(EntityTypeBuilder<Brand> builder)
	{
		builder.ToTable("brands");
		builder.HasKey(b => b.Id);
		builder.Property(b => b.Id)
			.HasIdentityConversion<BrandId, int>(v => new BrandId(v))
			.ValueGeneratedOnAdd();
		builder.Property(b => b.Name).IsRequired().HasMaxLength(128);
		builder.Property(b => b.Slug)
			.HasValueObjectConversion(s => s.Value, v => Slug.Create(v))
			.HasMaxLength(128)
			.IsRequired();
		builder.Property(b => b.LogoUrl).HasMaxLength(512);
		builder.HasIndex(b => b.Name).IsUnique();
		builder.HasIndex(b => b.Slug).IsUnique();
	}
}
