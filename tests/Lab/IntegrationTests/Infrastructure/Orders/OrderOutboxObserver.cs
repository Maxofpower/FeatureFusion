using System.Diagnostics;
using System.Text.Json;
using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Infrastructure;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IntegrationTests.Infrastructure.Orders;

/// <summary>
/// Lab-only observation of <c>outbox_messages</c> rows for
/// <see cref="OrderCreatedIntegrationEvent"/>. No assertions; callers interpret evidence.
/// </summary>
public static class OrderOutboxObserver
{
	public static string OrderCreatedEventType { get; } = nameof(OrderCreatedIntegrationEvent);

	public static async Task<IReadOnlyList<OrderOutboxRow>> FindByOrderIdAsync(
		IServiceProvider services,
		Guid orderId,
		CancellationToken cancellationToken = default)
	{
		await using var scope = services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var serializerOptions = scope.ServiceProvider
			.GetRequiredService<IOptions<EventBusSubscriptionInfo>>().Value.JsonSerializerOptions;

		var rows = await db.OutboxMessages
			.AsNoTracking()
			.Where(m => m.EventType == OrderCreatedEventType)
			.ToListAsync(cancellationToken);

		var matches = new List<OrderOutboxRow>();
		foreach (var row in rows)
		{
			if (row.Payload is null)
				continue;

			var evt = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(
				row.Payload,
				serializerOptions);
			if (evt is null || evt.OrderId != orderId)
				continue;

			matches.Add(Map(row, evt));
		}

		return matches;
	}

	/// <summary>
	/// Polls until exactly one outbox row exists for <paramref name="orderId"/> (any status).
	/// Default timeout 20s / 100ms delay — matches prior experiment-local waits.
	/// </summary>
	public static async Task<OrderOutboxRow> WaitUntilExistsAsync(
		IServiceProvider services,
		Guid orderId,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var limit = timeout ?? TimeSpan.FromSeconds(20);
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < limit)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var rows = await FindByOrderIdAsync(services, orderId, cancellationToken);
			if (rows.Count == 1)
				return rows[0];

			await Task.Delay(100, cancellationToken);
		}

		throw new TimeoutException(
			$"Outbox message for OrderId={orderId} was not observed within timeout");
	}

	/// <summary>
	/// Polls until exactly one outbox row exists and <see cref="OrderOutboxRow.WorkerProcessed"/> is true.
	/// Default timeout 20s / 100ms delay — matches prior experiment-local waits.
	/// </summary>
	public static async Task<OrderOutboxRow> WaitUntilProcessedAsync(
		IServiceProvider services,
		Guid orderId,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var limit = timeout ?? TimeSpan.FromSeconds(20);
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < limit)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var rows = await FindByOrderIdAsync(services, orderId, cancellationToken);
			if (rows.Count == 1 && rows[0].WorkerProcessed)
				return rows[0];

			await Task.Delay(100, cancellationToken);
		}

		throw new TimeoutException(
			$"Outbox message for OrderId={orderId} did not reach processed state within timeout");
	}

	/// <summary>
	/// Polls until at least one outbox row exists for <paramref name="orderId"/>.
	/// Default timeout 20s / 100ms delay — matches prior experiment-local waits.
	/// </summary>
	public static async Task<IReadOnlyList<OrderOutboxRow>> WaitUntilAnyAsync(
		IServiceProvider services,
		Guid orderId,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var limit = timeout ?? TimeSpan.FromSeconds(20);
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < limit)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var rows = await FindByOrderIdAsync(services, orderId, cancellationToken);
			if (rows.Count >= 1)
				return rows;

			await Task.Delay(100, cancellationToken);
		}

		throw new TimeoutException(
			$"Outbox message for OrderId={orderId} was not observed within timeout");
	}

	private static OrderOutboxRow Map(OutboxMessage row, OrderCreatedIntegrationEvent evt)
	{
		var workerPending = row.ProcessedAt is null;
		var workerProcessed = row.Status == MessageStatus.Processed
			&& row.ProcessedAt is not null
			&& row.CompletedAt is not null;

		return new OrderOutboxRow(
			OutboxMessageId: row.Id,
			IntegrationEventId: evt.Id,
			OrderId: evt.OrderId,
			Total: evt.Total,
			EventType: row.EventType,
			Status: row.Status.ToString(),
			ProcessedAtUtc: row.ProcessedAt,
			CompletedAtUtc: row.CompletedAt,
			RetryCount: row.RetryCount,
			WorkerPending: workerPending,
			WorkerProcessed: workerProcessed,
			CreatedAtUtc: row.CreatedAt);
	}
}

/// <summary>Observed outbox row for an order-created integration event.</summary>
public sealed record OrderOutboxRow(
	Guid OutboxMessageId,
	Guid IntegrationEventId,
	Guid OrderId,
	decimal Total,
	string EventType,
	string Status,
	DateTime? ProcessedAtUtc,
	DateTime? CompletedAtUtc,
	int RetryCount,
	bool WorkerPending,
	bool WorkerProcessed,
	DateTime CreatedAtUtc);
