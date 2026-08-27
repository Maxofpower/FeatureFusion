using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Extensions;
using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventBusRabbitMQ.Infrastructure.Context
{

	public class EventBusDbContext : DbContext
	{
		public EventBusDbContext(DbContextOptions<EventBusDbContext> options)
			: base(options)
		{
		}

		public DbSet<OutboxMessage> OutboxMessages { get; set; }
		public DbSet<InboxMessage> InboxMessages { get; set; }
		public DbSet<ProcessedMessage> ProcessedMessages { get; set; }
		public DbSet<InboxSubscriber> InboxSubscriber { get; set; }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			base.OnModelCreating(modelBuilder);

			modelBuilder.Entity<OutboxMessage>(entity =>
			{
				entity.ToTable("outbox_messages");

				// Primary Key
				entity.HasKey(e => e.Id)
					.HasName("pk_outbox_messages");
				entity.Property(e => e.Id)
					.HasColumnName("id")
					.ValueGeneratedNever();

				entity.Property(e => e.EventType)
					.HasColumnName("event_type")
					.HasMaxLength(256)
					.IsRequired();

				entity.Property(e => e.Status)
					.HasColumnName("status")
					.HasConversion<string>()
					.HasMaxLength(20)
					.IsRequired();

				entity.Property(e => e.CreatedAt)
					.HasColumnName("created_at")
					.IsRequired()
					.HasColumnType("timestamptz"); 

				entity.Property(e => e.ProcessedAt)
					.HasColumnName("processed_at")
					.HasColumnType("timestamptz");

				entity.Property(e => e.Payload)
					.HasColumnName("payload")
					.HasColumnType("jsonb") 
					.IsRequired();

				entity.Property(e => e.RetryCount)
					.HasColumnName("retry_count")
					.HasDefaultValue(0);

		
				entity.HasIndex(e => e.CreatedAt)
					.HasDatabaseName("ix_outbox_messages_unprocessed")
					.HasFilter("processed_at IS NULL")
					.IncludeProperties(e => new { e.Id, e.EventType, e.Payload });
				entity.HasIndex(e => new { e.Status, e.RetryCount, e.CreatedAt })
					.HasDatabaseName("ix_outbox_messages_status_retry_created")
					.HasFilter("status IN ('Pending', 'Failed')")
					.IncludeProperties(e => new { e.Id, e.EventType, e.Payload });
			});

			modelBuilder.Entity<InboxMessage>(entity =>
			{
				entity.ToTable("inbox_messages");
				entity.HasKey(e => e.Id);
				entity.Property(e => e.RowVersion)
					.IsRowVersion()
					.HasColumnName("xmin");

				entity.Property(e => e.Id).ValueGeneratedNever();
				entity.Property(e => e.EventType).HasMaxLength(256).IsRequired();
				entity.Property(e => e.Status)
					.HasConversion<string>()
					.HasMaxLength(20)
					.IsRequired();
				entity.Property(e => e.CreatedAt).IsRequired();
				entity.Property(e => e.Payload).IsRequired();
				entity.Property(e => e.ProcessedAt).IsRequired(false);

				entity.HasMany(e => e.Subscribers)
					.WithOne(s => s.Message)
					.HasForeignKey(s => s.MessageId);

				entity.HasIndex(e => e.Id).IsUnique();
			});
			modelBuilder.Entity<InboxSubscriber>(entity =>
			{
				entity.ToTable("inbox_subscribers");
				entity.HasKey(e => e.Id);

				entity.Property(e => e.SubscriberName).HasMaxLength(256).IsRequired();
				entity.Property(e => e.Status)
					.HasConversion<string>()
					.HasMaxLength(20)
					.IsRequired();
				entity.Property(e => e.Attempts).HasDefaultValue(0);
				entity.Property(e => e.LastAttemptedAt).IsRequired(false);
				entity.Property(e => e.Error).IsRequired(false);
				entity.HasIndex(e => new { e.MessageId, e.SubscriberName }).IsUnique();
				entity.HasIndex(e => e.Status);
			});
			modelBuilder.Entity<ProcessedMessage>(entity =>
			{
				entity.ToTable("processed_messages");
			});
			base.OnModelCreating(modelBuilder);
		}
	}
	public interface IEventStoreDbContext
	{
		DbSet<OutboxMessage> OutboxMessages { get; set; }
		DbSet<InboxMessage> InboxMessages { get; set; }
		DbSet<ProcessedMessage> ProcessedMessages { get; set; }
		DbSet<InboxSubscriber> InboxSubscriber { get; set; }
	}
}
