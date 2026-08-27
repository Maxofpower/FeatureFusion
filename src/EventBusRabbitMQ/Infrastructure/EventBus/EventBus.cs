using System.Diagnostics;
using System.Text.Json;
using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure.Messaging;
using EventBusRabbitMQ.Utilities;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EventBusRabbitMQ.Infrastructure.EventBus;

/// <summary>
/// RabbitMQ-backed integration event bus with transactional outbox support.
/// </summary>
public sealed class EventBus : IEventBus
{
	private readonly IRabbitMQPersistentConnection _persistentConnection;
	private readonly ILogger<EventBus> _logger;
	private readonly IServiceProvider _serviceProvider;
	private readonly EventBusOptions _options;
	private readonly EventBusSubscriptionInfo _subscriptionInfo;
	private IChannel? _consumerChannel;
	private bool _disposed;

	/// <summary>
	/// Creates a new <see cref="EventBus"/>.
	/// </summary>
	public EventBus(
		IRabbitMQPersistentConnection persistentConnection,
		ILogger<EventBus> logger,
		IServiceProvider serviceProvider,
		IOptions<EventBusOptions> options,
		IOptions<EventBusSubscriptionInfo> subscriptionInfo)
	{
		_persistentConnection = persistentConnection;
		_logger = logger;
		_serviceProvider = serviceProvider;
		_options = options.Value;
		_subscriptionInfo = subscriptionInfo.Value;
	}

	/// <inheritdoc />
	public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
		where TEvent : IntegrationEvent
	{
		await using var scope = _serviceProvider.CreateAsyncScope();
		var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();

		try
		{
			var storeResult = await outbox.StoreOutgoingMessageAsync(@event, ct).ConfigureAwait(false);

			if (storeResult == MessageStoreResult.Duplicate)
			{
				_logger.LogDebug("Duplicate message detected in outbox, publishing directly: {MessageId}", @event.Id);
				await PublishDirect(@event, ct).ConfigureAwait(false);
			}
			else if (storeResult == MessageStoreResult.StorageFailed)
			{
				_logger.LogWarning("Outbox storage failed for {MessageId}; attempting direct publish", @event.Id);
				await PublishDirect(@event, ct).ConfigureAwait(false);
			}
		}
		catch (Exception ex) when (ShouldFallbackToDirectPublish(@event))
		{
			_logger.LogWarning(ex, "Outbox failed, falling back to direct publish");
			await PublishDirect(@event, ct).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task PublishAsync<TEvent>(TEvent @event, IDbContextTransaction ts)
		where TEvent : IntegrationEvent
	{
		await using var scope = _serviceProvider.CreateAsyncScope();
		var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();

		try
		{
			var storeResult = await outbox.StoreOutgoingMessageAsync(@event, ts).ConfigureAwait(false);

			if (storeResult is MessageStoreResult.Duplicate or MessageStoreResult.StorageFailed)
			{
				_logger.LogDebug("Outbox result {Result} for {MessageId}; publishing directly", storeResult, @event.Id);
				await PublishDirect(@event).ConfigureAwait(false);
			}
		}
		catch (Exception ex) when (ShouldFallbackToDirectPublish(@event))
		{
			_logger.LogWarning(ex, "Outbox failed, falling back to direct publish");
			await PublishDirect(@event).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task PublishDirect<TEvent>(TEvent @event, CancellationToken ct = default)
		where TEvent : IntegrationEvent
	{
		await using var channel = await _persistentConnection.CreatePublisherChannelAsync(ct).ConfigureAwait(false);
		var props = RabbitMQMessageHelper.CreateBasicProperties(@event, _options.SubscriptionClientName);
		var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), _subscriptionInfo.JsonSerializerOptions);

		using var confirmCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		confirmCts.CancelAfter(RabbitMQConstants.DefaultConfirmTimeout);

		await channel.BasicPublishAsync(
			exchange: RabbitMQConstants.MainExchangeName,
			routingKey: @event.GetType().Name,
			mandatory: true,
			basicProperties: props,
			body: body,
			cancellationToken: confirmCts.Token).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task StartAsync(CancellationToken ct)
	{
		await InitializeConsumer(ct).ConfigureAwait(false);
		_logger.LogInformation("Started consuming from {QueueName}", _options.SubscriptionClientName);
	}

	private async Task ConfigureTopologyAsync(IChannel channel, CancellationToken ct)
	{
		try
		{
			await channel.ExchangeDeclareAsync(
				exchange: RabbitMQConstants.MainExchangeName,
				type: ExchangeType.Direct,
				durable: true,
				autoDelete: false,
				cancellationToken: ct).ConfigureAwait(false);

			await channel.ExchangeDeclareAsync(
				exchange: RabbitMQConstants.DeadLetterExchangeName,
				type: ExchangeType.Direct,
				durable: true,
				autoDelete: false,
				cancellationToken: ct).ConfigureAwait(false);

			var queueArgs = new Dictionary<string, object?>
			{
				[RabbitMQConstants.DeadLetterExchangeArg] = RabbitMQConstants.DeadLetterExchangeName,
				[RabbitMQConstants.MessageTtlArg] = _options.MessageTTL,
			};

			await channel.QueueDeclareAsync(
				queue: _options.SubscriptionClientName,
				durable: true,
				exclusive: false,
				autoDelete: false,
				arguments: queueArgs,
				cancellationToken: ct).ConfigureAwait(false);

			var dlqArgs = new Dictionary<string, object?>
			{
				[RabbitMQConstants.QueueModeArg] = RabbitMQConstants.LazyQueueMode,
			};

			await channel.QueueDeclareAsync(
				queue: $"{_options.SubscriptionClientName}{RabbitMQConstants.DeadLetterQueueSuffix}",
				durable: true,
				exclusive: false,
				autoDelete: false,
				arguments: dlqArgs,
				cancellationToken: ct).ConfigureAwait(false);

			foreach (var (eventName, _) in _subscriptionInfo.EventTypes)
			{
				await channel.QueueBindAsync(
					queue: _options.SubscriptionClientName,
					exchange: RabbitMQConstants.MainExchangeName,
					routingKey: eventName,
					cancellationToken: ct).ConfigureAwait(false);

				await channel.QueueBindAsync(
					queue: $"{_options.SubscriptionClientName}{RabbitMQConstants.DeadLetterQueueSuffix}",
					exchange: RabbitMQConstants.DeadLetterExchangeName,
					routingKey: eventName,
					cancellationToken: ct).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to configure RabbitMQ topology");
			throw;
		}
	}

	/// <inheritdoc />
	public async Task ValidateTopologyAsync(CancellationToken ct)
	{
		await using var channel = await _persistentConnection.CreateChannelAsync(ct).ConfigureAwait(false);

		try
		{
			await channel.ExchangeDeclarePassiveAsync(RabbitMQConstants.MainExchangeName, ct).ConfigureAwait(false);
			await channel.ExchangeDeclarePassiveAsync(RabbitMQConstants.DeadLetterExchangeName, ct).ConfigureAwait(false);
			await channel.QueueDeclarePassiveAsync(_options.SubscriptionClientName, ct).ConfigureAwait(false);
			await channel.QueueDeclarePassiveAsync(
				$"{_options.SubscriptionClientName}{RabbitMQConstants.DeadLetterQueueSuffix}", ct).ConfigureAwait(false);

			_logger.LogInformation("RabbitMQ topology validated successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "RabbitMQ topology validation failed");
			throw;
		}
	}

	/// <inheritdoc />
	public async Task ResetTopologyAsync(CancellationToken ct = default)
	{
		if (_consumerChannel is { IsOpen: true })
		{
			try
			{
				await _consumerChannel.QueueDeleteAsync(_options.SubscriptionClientName, ifUnused: false, ifEmpty: false, cancellationToken: ct)
					.ConfigureAwait(false);
				await _consumerChannel.QueueDeleteAsync(
						$"{_options.SubscriptionClientName}{RabbitMQConstants.DeadLetterQueueSuffix}",
						ifUnused: false,
						ifEmpty: false,
						cancellationToken: ct)
					.ConfigureAwait(false);
				await _consumerChannel.ExchangeDeleteAsync(RabbitMQConstants.MainExchangeName, ifUnused: false, cancellationToken: ct)
					.ConfigureAwait(false);
				await _consumerChannel.ExchangeDeleteAsync(RabbitMQConstants.DeadLetterExchangeName, ifUnused: false, cancellationToken: ct)
					.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error resetting RabbitMQ topology");
				throw;
			}
		}

		await InitializeConsumer(ct).ConfigureAwait(false);
	}

	private async Task MessagetHandler(object sender, BasicDeliverEventArgs args)
	{
		var messageId = RabbitMQMessageHelper.GetMessageId(args);
		var deliveryTag = args.DeliveryTag;
		var retryCount = RabbitMQMessageHelper.GetRetryCount(args);
		var attemptNumber = retryCount + 1;

		using var activity = StartActivity(args);
		try
		{
			await using var scope = _serviceProvider.CreateAsyncScope();
			var processor = scope.ServiceProvider.GetRequiredService<IMessageProcessor>();

			// Copy body — RabbitMQ.Client 7 owns the memory only for the duration of this handler.
			var bodyCopy = args.Body.ToArray();
			var argsCopy = new BasicDeliverEventArgs(
				consumerTag: args.ConsumerTag,
				deliveryTag: args.DeliveryTag,
				redelivered: args.Redelivered,
				exchange: args.Exchange,
				routingKey: args.RoutingKey,
				properties: args.BasicProperties,
				body: bodyCopy);

			var result = await processor.ProcessMessageAsync(argsCopy, _options.EnableDeduplication).ConfigureAwait(false);

			if (result == ProcessingResult.Success)
			{
				await SafeAckAsync(deliveryTag).ConfigureAwait(false);
				activity?.SetTag("message.status", "processed");
				_logger.LogInformation("Processed message {MessageId}", messageId);
			}
			else if (result == ProcessingResult.RetryLater && attemptNumber < _options.RetryCount + 1)
			{
				var delay = CalculateRetryDelay(attemptNumber);
				await SafeNackAsync(deliveryTag, requeue: true).ConfigureAwait(false);

				activity?.SetTag("message.status", "retrying");
				activity?.SetTag("message.retry_delay_ms", delay.TotalMilliseconds);

				_logger.LogWarning(
					"Retrying message {MessageId} (Attempt {AttemptNumber}/{MaxAttempts})",
					messageId, attemptNumber, _options.RetryCount + 1);
			}
			else
			{
				await SafeNackAsync(deliveryTag, requeue: false).ConfigureAwait(false);
				activity?.SetTag("message.status", "failed");
				_logger.LogError("Message {MessageId} moved to DLQ after {AttemptNumber} attempts",
					messageId, attemptNumber);
			}
		}
		catch (Exception ex)
		{
			activity?.AddException(ex);
			_logger.LogError(ex, "Critical error processing message {MessageId}", messageId);
			await SafeNackAsync(deliveryTag, requeue: false).ConfigureAwait(false);
		}
	}

	private static TimeSpan CalculateRetryDelay(int attemptNumber)
	{
		var maxDelay = TimeSpan.FromMinutes(5);
		var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attemptNumber));
		var jitter = Random.Shared.NextDouble() * 0.2;
		var delay = baseDelay * (1 + jitter);
		return delay > maxDelay ? maxDelay : delay;
	}

	private Activity? StartActivity(BasicDeliverEventArgs args)
	{
		var activity = new ActivitySource("EventBus").StartActivity("ProcessMessage");
		activity?.SetTag("message.id", args.BasicProperties.MessageId);
		activity?.SetTag("message.routing_key", args.RoutingKey);
		activity?.SetTag("message.retry_count", RabbitMQMessageHelper.GetRetryCount(args));
		return activity;
	}

	private async Task SafeAckAsync(ulong deliveryTag)
	{
		try
		{
			if (_consumerChannel is not null)
			{
				await _consumerChannel.BasicAckAsync(deliveryTag, multiple: false).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to ACK message");
		}
	}

	private async Task SafeNackAsync(ulong deliveryTag, bool requeue)
	{
		try
		{
			if (_consumerChannel is not null)
			{
				await _consumerChannel.BasicNackAsync(deliveryTag, multiple: false, requeue: requeue).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to NACK message");
		}
	}

	private static bool ShouldFallbackToDirectPublish<TEvent>(TEvent @event) where TEvent : IntegrationEvent =>
		@event is IAllowDirectFallback;

	private async Task InitializeConsumer(CancellationToken ct)
	{
		_consumerChannel = await _persistentConnection.CreateChannelAsync(ct).ConfigureAwait(false);
		await ConfigureTopologyAsync(_consumerChannel, ct).ConfigureAwait(false);

		var consumer = new AsyncEventingBasicConsumer(_consumerChannel);
		consumer.ReceivedAsync += MessagetHandler;

		await _consumerChannel.BasicConsumeAsync(
			queue: _options.SubscriptionClientName,
			autoAck: false,
			consumer: consumer,
			cancellationToken: ct).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		try
		{
			_consumerChannel?.Dispose();
			_logger.LogInformation("RabbitMQ event bus disposed");
		}
		catch (Exception ex)
		{
			_logger.LogCritical(ex, "Error disposing consumer channel");
		}
	}

	/// <inheritdoc />
	public IChannel? GetConsumerChannel() => _consumerChannel;
}

/// <summary>
/// Helpers for RabbitMQ message metadata.
/// </summary>
public static class RabbitMQMessageHelper
{
	/// <summary>
	/// Creates publish properties for an integration event.
	/// </summary>
	public static BasicProperties CreateBasicProperties(IntegrationEvent @event, string serviceName)
	{
		return new BasicProperties
		{
			DeliveryMode = DeliveryModes.Persistent,
			MessageId = @event.Id.ToString(),
			Headers = new Dictionary<string, object?>
			{
				[RabbitMQConstants.EventTypeHeader] = @event.GetType().Name,
				[RabbitMQConstants.OccurredOnHeader] = @event.CreationDate.ToString("O"),
				[RabbitMQConstants.SourceServiceHeader] = serviceName,
				[RabbitMQConstants.RetryCountHeaderKey] = 0,
			},
		};
	}

	/// <summary>
	/// Reads the message id from delivery properties.
	/// </summary>
	public static Guid GetMessageId(BasicDeliverEventArgs args) =>
		Guid.Parse(args.BasicProperties.MessageId ?? throw new InvalidOperationException("MessageId is required"));

	/// <summary>
	/// Reads the retry count header from delivery properties.
	/// </summary>
	public static int GetRetryCount(BasicDeliverEventArgs args)
	{
		if (args.BasicProperties.Headers?.TryGetValue(RabbitMQConstants.RetryCountHeaderKey, out var value) == true)
		{
			return value switch
			{
				int count => count,
				byte[] bytes when bytes.Length > 0 => bytes[0],
				long l => (int)l,
				_ => 0,
			};
		}

		return 0;
	}
}

/// <summary>
/// Context for error processing telemetry.
/// </summary>
public sealed class ErrorProcessingContext
{
	/// <summary>Current attempt number.</summary>
	public int AttemptNumber { get; set; }

	/// <summary>Maximum attempts allowed.</summary>
	public int MaxAttempts { get; set; }

	/// <summary>Optional message headers.</summary>
	public IDictionary<string, object?>? Headers { get; set; }
}
