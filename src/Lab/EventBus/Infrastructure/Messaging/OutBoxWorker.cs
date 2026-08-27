using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure;
using EventBusRabbitMQ.Infrastructure.EventBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Text.Json;


public class OutboxWorker<TDbContext> : BackgroundService where TDbContext : DbContext
{
	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger<OutboxWorker<TDbContext>> _logger;
	private readonly TimeSpan _interval = TimeSpan.FromSeconds(2);
	private readonly int BatchSize = 20;
	private const int MaxErrorLength = 500;


	public OutboxWorker(IServiceProvider serviceProvider, ILogger<OutboxWorker<TDbContext>> logger)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await ProcessPendingMessagesAsync(stoppingToken);
			}
			catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
			{
				// Schema not applied yet (migration hosted service still running / failed once).
				_logger.LogDebug(ex, "Outbox table not ready yet; will retry");
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.LogError(ex, "Error processing outbox messages");
			}

			await Task.Delay(_interval, stoppingToken);
		}
	}

	private async Task ProcessPendingMessagesAsync(CancellationToken stoppingToken)
	{
		using var scope = _serviceProvider.CreateScope();
		var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
		var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
		var subscriptionInfo = scope.ServiceProvider.GetRequiredService<IOptions<EventBusSubscriptionInfo>>();

		var messages = await dbContext.Set<OutboxMessage>()
			.AsNoTracking()
			.Where(m => m.ProcessedAt == null)
			.OrderBy(m => m.CreatedAt)
			.Take(BatchSize)
			.ToListAsync(stoppingToken);

		foreach (var message in messages)
		{
			try
			{
				if (!subscriptionInfo.Value.EventTypes.TryGetValue(message.EventType, out var eventType))
				{
					await MarkMessageAsFailedAsync(dbContext, message,
						$"Event type '{message.EventType}' not found in subscriptions or assemblies",
						stoppingToken);
					continue;
				}

				var @event = JsonSerializer.Deserialize(message.Payload!, eventType,
					subscriptionInfo.Value.JsonSerializerOptions) as IntegrationEvent;

				if (@event == null || @event.Id != message.Id)
				{
					await MarkMessageAsFailedAsync(dbContext, message,
						$"ID mismatch or null event (Stored: {message.Id}, Deserialized: {@event?.Id})",
						stoppingToken);
					continue;
				}

				await eventBus.PublishDirect((dynamic)@event, ct: stoppingToken);
				await MarkMessageAsProcessedAsync(dbContext, message, stoppingToken);
			}
			catch (JsonException jsonEx)
			{
				await HandleProcessingFailureAsync(dbContext, message,
					new InvalidOperationException($"JSON deserialization failed for {message.EventType}", jsonEx),
					stoppingToken);
			}
			catch (Exception ex)
			{
				await HandleProcessingFailureAsync(dbContext, message, ex, stoppingToken);
			}
		}
	}

	private async Task MarkMessageAsProcessedAsync(
		TDbContext dbContext,
		OutboxMessage message,
		CancellationToken ct)
	{
		message.Status = MessageStatus.Processed;
		message.ProcessedAt ??= DateTime.UtcNow;
		message.CompletedAt = DateTime.UtcNow;
		message.Error = null;
		dbContext.Update(message);
		await dbContext.SaveChangesAsync(ct);
	}

	private async Task MarkMessageAsFailedAsync(
		TDbContext dbContext,
		OutboxMessage message,
		string error,
		CancellationToken ct)
	{
		message.Status = MessageStatus.Failed;
		message.Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
		message.RetryCount++;
		dbContext.Update(message);
		await dbContext.SaveChangesAsync(ct);
	}

	private async Task HandleProcessingFailureAsync(
		TDbContext dbContext,
		OutboxMessage message,
		Exception ex,
		CancellationToken ct)
	{
		_logger.LogError(ex, "Failed to process outbox message {MessageId}", message.Id);
		await MarkMessageAsFailedAsync(dbContext, message, ex.Message, ct);
	}
}