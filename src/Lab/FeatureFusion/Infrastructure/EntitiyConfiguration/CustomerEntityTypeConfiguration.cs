using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class CustomerEntityTypeConfiguration : IEntityTypeConfiguration<Customer>
{
	public void Configure(EntityTypeBuilder<Customer> builder)
	{
		builder.ToTable("customers");
		builder.HasKey(c => c.Id);
		builder.Ignore(c => c.DomainEvents);
		builder.Ignore(c => c.OriginalVersion);

		builder.Property(c => c.Id)
			.HasIdentityConversion<CustomerId, int>(v => new CustomerId(v))
			.ValueGeneratedOnAdd();

		builder.Property(c => c.DisplayName).IsRequired().HasMaxLength(200);
		builder.Property(c => c.CreatedAt)
			.HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

		builder.Property(c => c.Email)
			.HasValueObjectConversion(e => e.Value, v => Email.Create(v))
			.HasColumnName("Email")
			.HasMaxLength(256)
			.IsRequired();

		builder.HasIndex("Email").IsUnique().HasDatabaseName("IX_customers_email");
	}
}
