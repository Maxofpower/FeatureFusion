using RabbitMQ.Client;

namespace EventBusRabbitMQ.Infrastructure.EventBus;

/// <summary>
/// Manages a resilient RabbitMQ connection and channel factory.
/// </summary>
public interface IRabbitMQPersistentConnection : IAsyncDisposable, IDisposable
{
	/// <summary>
	/// Gets a value indicating whether the underlying connection is open.
	/// </summary>
	bool IsConnected { get; }

	/// <summary>
	/// Creates a new AMQP channel with resilience policies applied.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel channel creation.</param>
	/// <returns>An open <see cref="IChannel"/>.</returns>
	Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a publisher channel with publisher confirms enabled.
	/// </summary>
	/// <param name="cancellationToken">Token used to cancel channel creation.</param>
	Task<IChannel> CreatePublisherChannelAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Attempts to establish a connection with retry logic.
	/// </summary>
	Task<bool> TryConnectAsync(CancellationToken cancellationToken = default);
}
