using BuildingBlocks.Domain.EntityFrameworkCore;
using FeatureFusion.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class PaymentRecordEntityTypeConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
	public void Configure(EntityTypeBuilder<PaymentRecord> builder)
	{
		builder.ToTable("payment_records");
		builder.HasKey(p => p.Id);

		builder.Property(p => p.Id)
			.HasIdentityConversion<PaymentRecordId, int>(v => new PaymentRecordId(v))
			.ValueGeneratedOnAdd();

		builder.Property(p => p.Outcome).HasConversion<string>().HasMaxLength(32);
		builder.Property(p => p.Amount).HasPrecision(18, 2);
		builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
		builder.Property(p => p.CreatedAtUtc)
			.HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

		builder.HasIndex(p => p.CorrelationOrderId)
			.HasDatabaseName("IX_payment_records_correlation");
	}
}
