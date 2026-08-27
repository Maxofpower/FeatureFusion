using EventBusRabbitMQ.Events;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EventBusRabbitMQ.Domain
{
	public abstract class MessageBase
	{
		[Key]
		public Guid Id { get; set; }

		[Required, MaxLength(200)]
		public required string EventType { get; set; }

		[Required]
		public byte[]? Payload { get; set; }

		[Required]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		public DateTime? ProcessedAt { get; set; }
		public DateTime? CompletedAt { get; set; }

		[Required, MaxLength(20)]
		public MessageStatus Status { get; set; }

		[MaxLength(500)]
		public string? Error { get; set; }
	}

	public class OutboxMessage : MessageBase
	{
		public int RetryCount { get; set; }

		public IntegrationEvent? GetEvent(JsonSerializerOptions options)
		{
			try
			{
				return JsonSerializer.Deserialize<IntegrationEvent>(Payload, options);
			}
			catch
			{
				return null;
			}
		}
	}

	public class InboxMessage : MessageBase
	{
		[Required]
		[MaxLength(200)]
		public required string ServiceName { get; set; }

		public bool IsProcessed { get; set; }

		// PostgreSQL-specific concurrency token using xmin system column
		[Timestamp]
		[Column("xmin")]
		public uint RowVersion { get; set; }

		// Navigation property for subscriber tracking
		public ICollection<InboxSubscriber> Subscribers { get; set; } = new List<InboxSubscriber>();


		public TEvent? GetEvent<TEvent>(JsonSerializerOptions options) where TEvent : IntegrationEvent
		{
			if (Payload == null) return null;
			return JsonSerializer.Deserialize<TEvent>(Payload, options);
		}
	}

	public class InboxSubscriber
	{
		[Key]
		public Guid Id { get; set; }

		[Required]
		[MaxLength(200)]
		public required string SubscriberName { get; set; }

		[Required]
		public Guid MessageId { get; set; }

		[ForeignKey("MessageId")]
		public required InboxMessage Message { get; set; }

		[Required]
		[MaxLength(20)]
		public MessageStatus Status { get; set; } = MessageStatus.Pending;

		public int Attempts { get; set; }

		public DateTime? LastAttemptedAt { get; set; }

		[MaxLength(500)]
		public string? Error { get; set; }

		[Timestamp]
		[Column("xmin")]
		public uint RowVersion { get; set; }
	}
	public class ProcessedMessage
	{
		[Key]
		public Guid Id { get; set; }
		public DateTime ProcessedAt { get; set; }
	}
	public enum MessageStatus
	{
		Pending,
		Processing,
		Processed,
		Failed,
		Archived
	}
}
