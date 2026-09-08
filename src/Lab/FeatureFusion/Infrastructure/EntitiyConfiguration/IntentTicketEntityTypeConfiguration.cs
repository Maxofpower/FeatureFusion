using FeatureFusion.Features.Admission;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFusion.Infrastructure.EntitiyConfiguration;

internal sealed class IntentTicketEntityTypeConfiguration : IEntityTypeConfiguration<IntentTicket>
{
	public void Configure(EntityTypeBuilder<IntentTicket> builder)
	{
		builder.ToTable("intent_tickets");
		builder.HasKey(t => t.Id);

		builder.Property(t => t.CapabilityId)
			.IsRequired()
			.HasMaxLength(128);

		builder.Property(t => t.RequestKey)
			.IsRequired()
			.HasMaxLength(256);

		builder.Property(t => t.IntentHash)
			.IsRequired()
			.HasMaxLength(128);

		builder.Property(t => t.IntentPayload)
			.IsRequired()
			.HasColumnType("text");

		builder.Property(t => t.Status)
			.IsRequired()
			.HasConversion<string>()
			.HasMaxLength(32);

		builder.Property(t => t.ReleasedBy)
			.HasMaxLength(128);

		builder.HasIndex(t => new { t.CapabilityId, t.RequestKey })
			.IsUnique()
			.HasDatabaseName("IX_intent_tickets_capability_request_key");

		builder.HasIndex(t => t.Status)
			.HasDatabaseName("IX_intent_tickets_status");
	}
}
