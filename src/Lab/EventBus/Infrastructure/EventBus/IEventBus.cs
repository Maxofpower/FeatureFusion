using EventBusRabbitMQ.Events;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace EventBusRabbitMQ.Infrastructure.EventBus;

/// <summary>
/// Publishes and consumes integration events over RabbitMQ (outbox/inbox, DLQ, Aspire-friendly).
/// </summary>
/// <remarks>
/// Catalog: docs/linkedin-posts.md. Sibling in-process pub/sub is a separate lab (not Mediator Publish).
/// </remarks>
public partial interface IEventBus : IHostedService, IDisposable
{
	/// <summary>
	/// Publishes an integration event through the transactional outbox when available.
	/// </summary>
	Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
		where TEvent : IntegrationEvent;

	/// <summary>
	/// Publishes an integration event within an existing EF Core transaction.
	/// </summary>
	Task PublishAsync<TEvent>(TEvent @event, IDbContextTransaction transaction)
		where TEvent : IntegrationEvent;

	/// <summary>
	/// Publishes directly to RabbitMQ, bypassing the outbox store path on failure fallbacks.
	/// </summary>
	Task PublishDirect<TEvent>(TEvent @event, CancellationToken ct = default)
		where TEvent : IntegrationEvent;

	/// <summary>
	/// Gets the active consumer channel, if any.
	/// </summary>
	IChannel? GetConsumerChannel();

	/// <summary>
	/// Deletes and recreates topology, then restarts the consumer.
	/// </summary>
	Task ResetTopologyAsync(CancellationToken ct = default);

	/// <summary>
	/// Validates that expected exchanges and queues exist.
	/// </summary>
	Task ValidateTopologyAsync(CancellationToken ct = default);
}
