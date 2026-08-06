namespace EventBusRabbitMQ.Infrastructure;

/// <summary>
/// Shared RabbitMQ topology and header constants.
/// </summary>
public static class RabbitMQConstants
{
	/// <summary>Primary domain events exchange.</summary>
	public const string MainExchangeName = "domain_events";

	/// <summary>Dead-letter exchange.</summary>
	public const string DeadLetterExchangeName = "domain_events_dlx";

	/// <summary>Outbox exchange name.</summary>
	public const string OutboxExchangeName = "domain_events_outbox";

	/// <summary>Default message TTL in milliseconds (24 hours).</summary>
	public const int DefaultMessageTTL = 86400000;

	/// <summary>Default consumer prefetch count.</summary>
	public const int DefaultPrefetchCount = 10;

	/// <summary>Publisher confirm timeout.</summary>
	public static readonly TimeSpan DefaultConfirmTimeout = TimeSpan.FromSeconds(10);

	/// <summary>Suffix appended to subscription queues for dead-letter queues.</summary>
	public const string DeadLetterQueueSuffix = "_dlq";

	/// <summary>Queue argument key for dead-letter exchange.</summary>
	public const string DeadLetterExchangeArg = "x-dead-letter-exchange";

	/// <summary>Queue argument key for message TTL.</summary>
	public const string MessageTtlArg = "x-message-ttl";

	/// <summary>Queue argument key for queue mode.</summary>
	public const string QueueModeArg = "x-queue-mode";

	/// <summary>Lazy queue mode value.</summary>
	public const string LazyQueueMode = "lazy";

	/// <summary>Event type header.</summary>
	public const string EventTypeHeader = "Event-Type";

	/// <summary>Occurred-on header.</summary>
	public const string OccurredOnHeader = "Occurred-On";

	/// <summary>Source service header.</summary>
	public const string SourceServiceHeader = "Source-Service";

	/// <summary>Message id header.</summary>
	public const string MessageIdHeader = "Message-Id";

	/// <summary>Retry count header (legacy name).</summary>
	public const string RetryCountHeader = "Retry-Count";

	/// <summary>Retry count header used on the wire.</summary>
	public const string RetryCountHeaderKey = "x-retry-count";
}
