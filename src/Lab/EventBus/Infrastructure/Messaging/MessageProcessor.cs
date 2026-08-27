using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure.EventBus;
using EventBusRabbitMQ.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EventBusRabbitMQ.Infrastructure.Messaging
{
	public interface IMessageProcessor
	{
		Task<ProcessingResult> ProcessMessageAsync(
			BasicDeliverEventArgs args,
			bool deduplication);
	}

	public class MessageProcessor : IMessageProcessor
	{
		private readonly ILogger<MessageProcessor> _logger;
		private readonly EventBusSubscriptionInfo _subscriptionInfo;
		private readonly IServiceProvider _serviceProvider;
		private readonly EventBusOptions _options;
		public MessageProcessor(
			ILogger<MessageProcessor> logger,
			IOptions<EventBusSubscriptionInfo> subscriptionInfo,
			IOptions<EventBusOptions> options,
		IServiceProvider serviceProvider)
		{
			_logger = logger;
			_subscriptionInfo = subscriptionInfo.Value;
			_serviceProvider = serviceProvider;
			_options = options.Value;
		}

		public async Task<ProcessingResult> ProcessMessageAsync(
			BasicDeliverEventArgs args,
			bool deduplication)
		{
			var eventName = args.RoutingKey;
			var messageId = RabbitMQMessageHelper.GetMessageId(args);
			await using var scope = _serviceProvider.CreateAsyncScope();
			var inbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();
			if (deduplication)
			{
				var dedupService = scope.ServiceProvider.GetRequiredService<IMessageDeduplicationService>();
				if (await dedupService.IsDuplicateAsync(messageId))
				{
					_logger.LogDebug("Message {MessageId} is a duplicate", messageId);
					return ProcessingResult.Success;
				}

			}
			MessageStoreResult dedupResult = await inbox.IsDuplicateAsync(messageId);
			if (dedupResult == MessageStoreResult.Duplicate)
			{
				_logger.LogDebug("Message {MessageId} is a duplicate", messageId);
				return ProcessingResult.Success;
			}

			if (!_subscriptionInfo.EventTypes.TryGetValue(eventName, out var eventType))
			{
				_logger.LogError("Unregistered event type: {EventName}", eventName);
				return ProcessingResult.PermanentFailure;
			}

			IntegrationEvent? @event;
			try
			{
				@event = JsonSerializer.Deserialize(Encoding.UTF8.GetString(args.Body.Span), eventType,
					   _subscriptionInfo.JsonSerializerOptions) as IntegrationEvent;

				if (@event == null)
				{
					_logger.LogError("Deserialization returned null for {EventName}", eventName);
					return ProcessingResult.PermanentFailure;
				}
				if (@event.Id != messageId)
				{
					_logger.LogError("Event ID {EventId} doesn't match message ID {MessageId}",
						@event.Id, messageId);
					return ProcessingResult.PermanentFailure;
				}
			}
			catch (JsonException ex)
			{
				_logger.LogError(ex, "Failed to deserialize {EventName}", eventName);
				return ProcessingResult.PermanentFailure;
			}

			try
			{
				var storeResult = await inbox.StoreIncomingMessageAsync(
					messageId,
					eventName,
					args.Body.ToArray(),
					_options.SubscriptionClientName);

				if (storeResult == MessageStoreResult.Duplicate)
				{
					return ProcessingResult.Success;
				}

				if (storeResult == MessageStoreResult.StorageFailed)
				{
					return ProcessingResult.RetryLater;
				}

				if (storeResult == MessageStoreResult.NoSubscribers)
				{
					return ProcessingResult.PermanentFailure;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Outbox storage failed for {MessageId}", messageId);
				return ProcessingResult.RetryLater;
			}

			try
			{
				var handlerResult = await DispatchToHandlers(scope.ServiceProvider, eventType, @event, inbox);

				if (deduplication)
				{
					var dedupService = scope.ServiceProvider.GetRequiredService<IMessageDeduplicationService>();
					if (handlerResult == ProcessingResult.Success)
					{
						await dedupService.MarkAsProcessedAsync(messageId);
					}
				}
				if (handlerResult == ProcessingResult.Success)
				{
					await inbox.MarkMessageAsProcessedAsync(messageId);
				}

				return handlerResult;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Unexpected error processing message");
				return ProcessingResult.PermanentFailure;
			}
		}

		private async Task<ProcessingResult> DispatchToHandlers(
			IServiceProvider sp,
			Type eventType,
			IntegrationEvent @event,
			ITransactionalOutbox inbox)
		{
			var handlers = sp.GetKeyedServices<IIntegrationEventHandler>(eventType).ToList();
			if (handlers.Count == 0)
			{
				return ProcessingResult.PermanentFailure;
			}

			var resultStatuses = new List<(string handlerType, ProcessingResult result, Guid messageId)>();

			foreach (var handler in handlers)
			{
				try
				{

					await handler.Handle(@event);

					resultStatuses.Add((handler.GetType().Name, ProcessingResult.Success, @event.Id));
				}
				catch (Exception ex) when (ex is BusinessException or TransientException)
				{
					resultStatuses.Add((handler.GetType().Name, ex is TransientException ? ProcessingResult.RetryLater : ProcessingResult.PermanentFailure, @event.Id));
				}
				catch (Exception)
				{
					resultStatuses.Add((handler.GetType().Name, ProcessingResult.PermanentFailure, @event.Id));
				}
			}

			await inbox.UpdateHandlerStatuses(resultStatuses);

			return ProcessingResult.Success;
		}

	}
}
