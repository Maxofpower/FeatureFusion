using EventBusRabbitMQ.Infrastructure.EventBus;
using Microsoft.Extensions.Logging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace EventBusRabbitMQ.Infrastructure.EventBus;

/// <summary>
/// Manages a persistent RabbitMQ connection with automatic recovery and resilience policies.
/// </summary>
public sealed class RabbitMQPersistentConnection : IRabbitMQPersistentConnection
{
	private readonly IConnectionFactory _connectionFactory;
	private readonly IResiliencePipelineProvider _policyProvider;
	private readonly ILogger<RabbitMQPersistentConnection> _logger;
	private readonly SemaphoreSlim _connectionLock = new(1, 1);

	private IConnection? _connection;
	private bool _disposed;

	/// <inheritdoc />
	public bool IsConnected => _connection is { IsOpen: true } && !_disposed;

	/// <summary>
	/// Initializes a new persistent RabbitMQ connection handler.
	/// </summary>
	public RabbitMQPersistentConnection(
		IConnectionFactory connectionFactory,
		IResiliencePipelineProvider policyProvider,
		ILogger<RabbitMQPersistentConnection> logger)
	{
		_connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
		_policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <inheritdoc />
	public Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default) =>
		CreateChannelCoreAsync(publisherConfirms: false, cancellationToken);

	/// <inheritdoc />
	public Task<IChannel> CreatePublisherChannelAsync(CancellationToken cancellationToken = default) =>
		CreateChannelCoreAsync(publisherConfirms: true, cancellationToken);

	private async Task<IChannel> CreateChannelCoreAsync(bool publisherConfirms, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

		return await _policyProvider.GetChannelPolicy().ExecuteAsync(async ct =>
		{
			var options = new CreateChannelOptions(
				publisherConfirmationsEnabled: publisherConfirms,
				publisherConfirmationTrackingEnabled: publisherConfirms);

			var channel = await _connection!.CreateChannelAsync(options, ct).ConfigureAwait(false);
			_logger.LogDebug("Created channel #{ChannelNumber} (publisherConfirms={PublisherConfirms})",
				channel.ChannelNumber, publisherConfirms);
			return channel;
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public async Task<bool> TryConnectAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (IsConnected)
			{
				return true;
			}

			_logger.LogInformation("Attempting RabbitMQ connection...");
			_connection = await CreateConnectionWithRetryAsync(cancellationToken).ConfigureAwait(false);
			SetupConnectionEvents();
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "RabbitMQ connection failed");
			return false;
		}
		finally
		{
			_connectionLock.Release();
		}
	}

	private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
	{
		if (!IsConnected && !await TryConnectAsync(cancellationToken).ConfigureAwait(false))
		{
			throw new InvalidOperationException("No RabbitMQ connection available");
		}
	}

	private async Task<IConnection> CreateConnectionWithRetryAsync(CancellationToken cancellationToken)
	{
		return await _policyProvider.GetConnectionPolicy().ExecuteAsync(async ct =>
		{
			var conn = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);

			if (!conn.IsOpen)
			{
				throw new BrokerUnreachableException(new InvalidOperationException("broker connection unreachable"));
			}

			_logger.LogInformation("Successfully connected to RabbitMQ");
			return conn;
		}, cancellationToken).ConfigureAwait(false);
	}

	private void SetupConnectionEvents()
	{
		if (_connection is null)
		{
			return;
		}

		_connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
		_connection.CallbackExceptionAsync += OnCallbackExceptionAsync;
		_connection.ConnectionBlockedAsync += OnConnectionBlockedAsync;
	}

	private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs e)
	{
		if (_disposed)
		{
			return Task.CompletedTask;
		}

		_logger.LogWarning("Connection shutdown: {ReplyText} (InitiatedBy: {Initiator})", e.ReplyText, e.Initiator);
		TryReconnect();
		return Task.CompletedTask;
	}

	private Task OnCallbackExceptionAsync(object sender, CallbackExceptionEventArgs e)
	{
		if (_disposed)
		{
			return Task.CompletedTask;
		}

		_logger.LogWarning(e.Exception, "Connection callback exception");
		TryReconnect();
		return Task.CompletedTask;
	}

	private Task OnConnectionBlockedAsync(object sender, ConnectionBlockedEventArgs e)
	{
		if (_disposed)
		{
			return Task.CompletedTask;
		}

		_logger.LogWarning("Connection blocked: {Reason}", e.Reason);
		return Task.CompletedTask;
	}

	private void TryReconnect()
	{
		if (_disposed)
		{
			return;
		}

		_ = Task.Run(async () =>
		{
			await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

			await Policy.Handle<Exception>()
				.WaitAndRetryForeverAsync(
					attempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 30)),
					(ex, delay) => _logger.LogWarning(ex, "Reconnect attempt failed. Next retry in {Delay}s", delay.TotalSeconds))
				.ExecuteAsync(async () =>
				{
					var reconnected = await TryConnectAsync().ConfigureAwait(false);
					if (reconnected)
					{
						_logger.LogInformation("Reconnected successfully");
					}

					return reconnected;
				}).ConfigureAwait(false);
		});
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		try
		{
			await _connectionLock.WaitAsync().ConfigureAwait(false);
			try
			{
				if (_connection is not null)
				{
					await _connection.CloseAsync().ConfigureAwait(false);
					await _connection.DisposeAsync().ConfigureAwait(false);
				}

				_logger.LogInformation("Connection disposed");
			}
			finally
			{
				_connectionLock.Release();
			}
		}
		catch (Exception ex)
		{
			_logger.LogCritical(ex, "Error during disposal");
		}
		finally
		{
			_connectionLock.Dispose();
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, nameof(RabbitMQPersistentConnection));
	}
}
