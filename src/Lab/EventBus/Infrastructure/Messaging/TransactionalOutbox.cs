using System.Collections.Generic;
using System.Data;
using System.Reflection.Emit;
using System.Text.Json;
using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure.Context;
using EventBusRabbitMQ.Infrastructure.EventBus;
using EventBusRabbitMQ.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace EventBusRabbitMQ.Infrastructure.Messaging
{
	public class TransactionalOutbox<TDbContext> : ITransactionalOutbox
	   where TDbContext : DbContext, IEventStoreDbContext

	{
		private readonly TDbContext _dbContext;
		private readonly JsonSerializerOptions _serializerOptions;
		private IDbContextTransaction? _transaction;
		private readonly ILogger<TransactionalOutbox<TDbContext>> _logger;
		private readonly IServiceProvider _serviceProvider;

		public TransactionalOutbox(
			TDbContext dbContext,
			IOptions<EventBusSubscriptionInfo> subscriptionInfo,
			ILogger<TransactionalOutbox<TDbContext>> logger,
			 IServiceProvider serviceProvider
		)
		{
			_dbContext = dbContext;
			_serializerOptions = subscriptionInfo.Value.JsonSerializerOptions;
			_logger = logger;
			_serviceProvider = serviceProvider;
		}

		public async Task<IDbContextTransaction> BeginTransactionAsync()
		{
			_transaction = await _dbContext.Database.BeginTransactionAsync();
			return _transaction;
		}

		public async Task CommitAsync()
		{
			if (_transaction != null)
			{
				await _dbContext.SaveChangesAsync();
				await _transaction.CommitAsync();
			}
		}

		public async Task RollbackAsync()
		{
			if (_transaction != null)
			{
				await _transaction.RollbackAsync();
			}
		}

		public async Task<MessageStoreResult> StoreOutgoingMessageAsync<TEvent>(TEvent @event, CancellationToken ct = default)
	where TEvent : IntegrationEvent
		{

			try
			{
				// First check if message exists
				var exists = await _dbContext.OutboxMessages
					.AsNoTracking()
					.AnyAsync(m => m.Id == @event.Id, ct);

				if (exists)
				{
					_logger.LogDebug("Duplicate message detected with ID: {MessageId}", @event.Id);
					return MessageStoreResult.Duplicate;
				}

				var message = new OutboxMessage
				{
					Id = @event.Id,
					EventType = @event.GetType().Name,
					Payload = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), _serializerOptions),
					Status = MessageStatus.Pending,
					CreatedAt = @event.CreationDate
				};

				await ResilientTransaction.New(_dbContext).ExecuteAsync(async () =>
				{
					await _dbContext.OutboxMessages.AddAsync(message, ct);
					await _dbContext.SaveChangesAsync(ct);
					return MessageStoreResult.Success;
				});

				return MessageStoreResult.Success;
			}
			catch (DbUpdateException ex) when (ex.IsDuplicateKeyError())
			{
				_logger.LogWarning("Duplicate message detected (database constraint) with ID: {MessageId}", @event.Id);
				return MessageStoreResult.Duplicate;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to store outgoing message with ID: {MessageId}", @event.Id);
				return MessageStoreResult.StorageFailed;
			}
		}
		public async Task<MessageStoreResult> StoreOutgoingMessageAsync<TEvent>(
	TEvent @event,
	IDbContextTransaction ts)
	where TEvent : IntegrationEvent
		{
			if (ts == null) throw new ArgumentNullException(nameof(ts));

			try
			{		
				var message = new OutboxMessage
				{
					Id = @event.Id,
					EventType = @event.GetType().Name,
					Payload = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), _serializerOptions),
					Status = MessageStatus.Pending,
					CreatedAt = @event.CreationDate
				};

				//	_dbContext.Database.UseTransaction(ts.GetDbTransaction()); // when there is multiple contexts

				_dbContext.Set<OutboxMessage>().Add(message);

				await _dbContext.SaveChangesAsync();
				return MessageStoreResult.Success;

			}
			catch (DbUpdateException ex) when (ex.IsDuplicateKeyError())
			{
				_logger.LogWarning("Duplicate message detected (database constraint) with ID: {MessageId}", @event.Id);
				return MessageStoreResult.Duplicate;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to store outgoing message with ID: {MessageId}", @event.Id);
				return MessageStoreResult.StorageFailed;
			}
		}
		public async Task<MessageStoreResult> StoreIncomingMessageAsync(
	Guid messageId,
	string eventType,
	byte[] payload,
	string serviceName)
		{
			try
			{
				using var scope = _serviceProvider.CreateScope();
				var subscriptionInfo = scope.ServiceProvider
					.GetRequiredService<IOptions<EventBusSubscriptionInfo>>().Value;

				List<string> subscriberNames = GetSubscribersForEventType(subscriptionInfo, eventType)
					?? new List<string>();

				if (subscriberNames.Count == 0)
				{
					_logger.LogWarning("No subscribers found for event type {EventType}", eventType);
					return MessageStoreResult.NoSubscribers;
				}

				// Null check for payload
				if (payload == null)
				{
					_logger.LogError("Payload cannot be null for message {MessageId}", messageId);
					return MessageStoreResult.StorageFailed;
				}

				if (await _dbContext.InboxMessages
					.AsNoTracking()
					.AnyAsync(m => m.Id == messageId))
				{
					return MessageStoreResult.Duplicate;
				}

				var message = new InboxMessage
				{
					Id = messageId,
					EventType = eventType ?? throw new ArgumentNullException(nameof(eventType)),
					Payload = payload,
					ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName)),
					Status = MessageStatus.Pending,
					CreatedAt = DateTime.UtcNow,
					Subscribers = new List<InboxSubscriber>()
				};

				foreach (var subscriberName in subscriberNames)
				{
					if (string.IsNullOrWhiteSpace(subscriberName))
					{
						_logger.LogWarning("Skipping empty subscriber name for message {MessageId}", messageId);
						continue;
					}

					message.Subscribers.Add(new InboxSubscriber
					{
						Id = Guid.NewGuid(),
						SubscriberName = subscriberName,
						Status = MessageStatus.Pending,
						Attempts = 0,
						Message = message,
						MessageId = message.Id
					});
				}

				if (message.Subscribers.Count == 0)
				{
					_logger.LogError("No valid subscribers found for message {MessageId}", messageId);
					return MessageStoreResult.NoSubscribers;
				}

				var success = await ResilientTransaction.New(_dbContext).ExecuteAsync(async () =>
				{
					_dbContext.InboxMessages.Add(message);
					await _dbContext.SaveChangesAsync();
					return true;
				});

				return success ? MessageStoreResult.Success : MessageStoreResult.StorageFailed;
			}
			catch (DbUpdateException ex) when (ex.IsDuplicateKeyError())
			{
				_logger.LogDebug("Duplicate message detected (race condition) for {MessageId}", messageId);
				return MessageStoreResult.Duplicate;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to store incoming message {MessageId}", messageId);
				return MessageStoreResult.StorageFailed;
			}
		}

		public async Task<MessageStoreResult> IsDuplicateAsync(Guid messageId)
		{
			//var cutoff = DateTime.UtcNow - _deduplicationWindow; //if needed

			if (await _dbContext.InboxMessages
				.AsNoTracking()
				.AnyAsync(x => x.Id == messageId))
				return MessageStoreResult.Duplicate;
			return MessageStoreResult.Success;
		}


		public async Task MarkMessageAsProcessedAsync(Guid messageId)
		{
			var message = await _dbContext.InboxMessages
				.FirstOrDefaultAsync(m => m.Id == messageId);

			if (message != null)
			{
				message.Status = MessageStatus.Processed;
				message.ProcessedAt = DateTime.UtcNow;
				message.IsProcessed = true;
				await _dbContext.SaveChangesAsync();
			}
		}
		
		public async Task UpdateHandlerStatuses(List<(string handlerType, ProcessingResult result,Guid messageID)> resultStatuses)
		{
			try
			{
			
				foreach (var (handlerType, result,messageId) in resultStatuses)
				{
					
						var handlerRecord = await _dbContext.InboxSubscriber
							.FirstOrDefaultAsync(h => h.SubscriberName == handlerType && h.MessageId==messageId); 
						if (handlerRecord != null)
						{
							if (result == ProcessingResult.Success)
							{
								handlerRecord.Status = MessageStatus.Processed;
							}
							else
							{
								handlerRecord.Status = MessageStatus.Failed;
							}
						}
					
				}
				await  _dbContext.SaveChangesAsync();
			}
			catch
			{
				// we cant re throw here 
			}
			
		}
		private List<string> GetSubscribersForEventType(EventBusSubscriptionInfo subscriptionInfo, string eventType)
		{
			var subscribers = new List<string>();

			if (string.IsNullOrWhiteSpace(eventType))
			{
				return subscribers;
			}

			if (subscriptionInfo.EventTypes.TryGetValue(eventType, out var eventClrType))
			{
				try
				{
					var handlers = _serviceProvider.GetKeyedServices<IIntegrationEventHandler>(eventClrType);

					foreach (var handler in handlers)
					{
						if (handler == null) continue;

						var serviceName = handler.GetType().Name;
						if (!string.IsNullOrWhiteSpace(serviceName))
						{
							subscribers.Add(serviceName);
						}
					}
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Failed to get subscribers for event type {EventType}", eventType);
				}
			}

			return subscribers.Distinct().ToList();
		}

		public async ValueTask DisposeAsync()
		{
			if (_transaction != null)
			{
				await _transaction.DisposeAsync();
			}
			GC.SuppressFinalize(this);
		}

	}

}