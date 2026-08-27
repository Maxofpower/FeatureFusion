using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace EventBusRabbitMQ.Infrastructure.EventBus;

/// <summary>
/// Provides cached Polly resilience policies for RabbitMQ connection, channel, publish, and consume operations.
/// </summary>
public interface IResiliencePipelineProvider
{
	/// <summary>Gets the connection establishment policy.</summary>
	IAsyncPolicy<IConnection> GetConnectionPolicy();

	/// <summary>Gets the channel creation policy.</summary>
	IAsyncPolicy<IChannel> GetChannelPolicy();

	/// <summary>Gets the publishing policy.</summary>
	IAsyncPolicy GetPublishingPolicy();

	/// <summary>Gets the consuming policy.</summary>
	IAsyncPolicy GetConsumingPolicy();
}

/// <summary>
/// Factory for RabbitMQ resilience policies.
/// </summary>
public sealed class ResiliencePipelineFactory : IResiliencePipelineProvider
{
	private readonly ResilienceOptions _options;
	private readonly ILogger<ResiliencePipelineFactory> _logger;
	private readonly ConcurrentDictionary<string, IAsyncPolicy> _nonGenericCache = new();
	private readonly ConcurrentDictionary<string, object> _genericCache = new();

	/// <summary>
	/// Creates a new <see cref="ResiliencePipelineFactory"/>.
	/// </summary>
	public ResiliencePipelineFactory(
		IOptions<ResilienceOptions> options,
		ILogger<ResiliencePipelineFactory> logger)
	{
		_options = options.Value;
		_logger = logger;
	}

	/// <inheritdoc />
	public IAsyncPolicy<IConnection> GetConnectionPolicy() =>
		GetOrCreateGenericPolicy("connection", CreateConnectionPolicy);

	/// <inheritdoc />
	public IAsyncPolicy<IChannel> GetChannelPolicy() =>
		GetOrCreateGenericPolicy("channel", CreateChannelPolicy);

	/// <inheritdoc />
	public IAsyncPolicy GetPublishingPolicy() =>
		_nonGenericCache.GetOrAdd("publish", _ => CreatePublishingPolicy());

	/// <inheritdoc />
	public IAsyncPolicy GetConsumingPolicy() =>
		_nonGenericCache.GetOrAdd("consume", _ => CreateConsumingPolicy());

	private IAsyncPolicy<T> GetOrCreateGenericPolicy<T>(string policyKey, Func<IAsyncPolicy<T>> policyFactory)
	{
		if (_genericCache.TryGetValue(policyKey, out var cachedPolicy) && cachedPolicy is IAsyncPolicy<T> typedPolicy)
		{
			return typedPolicy;
		}

		var newPolicy = policyFactory().WithPolicyKey(policyKey);
		_genericCache[policyKey] = newPolicy;
		return newPolicy;
	}

	private IAsyncPolicy<IConnection> CreateConnectionPolicy()
	{
		return Policy<IConnection>
			.Handle<BrokerUnreachableException>()
			.Or<SocketException>()
			.Or<TimeoutException>()
			.Or<AlreadyClosedException>()
			.WaitAndRetryAsync(
				retryCount: _options.ConnectionRetryCount,
				sleepDurationProvider: attempt => CalculateBackoff(attempt),
				onRetry: (_, delay, retryCount, _) =>
				{
					_logger.LogWarning("Connection retry {RetryCount} after {DelayMs}ms",
						retryCount, delay.TotalMilliseconds);
				})
			.WrapAsync(Policy.TimeoutAsync<IConnection>(
				seconds: _options.ConnectionTimeoutSeconds,
				timeoutStrategy: TimeoutStrategy.Pessimistic));
	}

	private IAsyncPolicy<IChannel> CreateChannelPolicy()
	{
		return Policy<IChannel>
			.Handle<BrokerUnreachableException>()
			.Or<SocketException>()
			.Or<TimeoutException>()
			.Or<AlreadyClosedException>()
			.WaitAndRetryAsync(
				retryCount: _options.ChannelRetryCount,
				sleepDurationProvider: attempt => CalculateBackoff(attempt));
	}

	private IAsyncPolicy CreatePublishingPolicy()
	{
		return Policy
			.Handle<BrokerUnreachableException>()
			.Or<SocketException>()
			.Or<TimeoutException>()
			.WaitAndRetryAsync(
				retryCount: _options.PublishRetryCount,
				sleepDurationProvider: attempt => CalculateBackoff(attempt))
			.WrapAsync(Policy
				.Handle<Exception>()
				.CircuitBreakerAsync(
					exceptionsAllowedBeforeBreaking: _options.CircuitBreakerThreshold,
					durationOfBreak: TimeSpan.FromSeconds(_options.CircuitBreakerDuration)));
	}

	private IAsyncPolicy CreateConsumingPolicy()
	{
		return Policy
			.Handle<Exception>()
			.WaitAndRetryForeverAsync(
				sleepDurationProvider: attempt => CalculateBackoff(attempt));
	}

	private static TimeSpan CalculateBackoff(int attempt)
	{
		var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attempt, 8)));
		var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
		return baseDelay + jitter;
	}
}

/// <summary>
/// Options controlling RabbitMQ resilience policies.
/// </summary>
public sealed class ResilienceOptions
{
	/// <summary>Connection retry attempts.</summary>
	public int ConnectionRetryCount { get; set; } = 5;

	/// <summary>Connection timeout in seconds.</summary>
	public int ConnectionTimeoutSeconds { get; set; } = 30;

	/// <summary>Channel creation retry attempts.</summary>
	public int ChannelRetryCount { get; set; } = 3;

	/// <summary>Publish retry attempts.</summary>
	public int PublishRetryCount { get; set; } = 3;

	/// <summary>Circuit breaker failure threshold.</summary>
	public int CircuitBreakerThreshold { get; set; } = 10;

	/// <summary>Circuit breaker open duration in seconds.</summary>
	public int CircuitBreakerDuration { get; set; } = 30;
}
