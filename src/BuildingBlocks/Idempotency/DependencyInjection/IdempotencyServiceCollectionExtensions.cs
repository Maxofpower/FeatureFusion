using BuildingBlocks.Idempotency.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace BuildingBlocks.Idempotency.DependencyInjection;

/// <summary>
/// Fluent registration returned by <see cref="IdempotencyServiceCollectionExtensions.AddBuildingBlocksIdempotency"/>.
/// </summary>
public sealed class IdempotencyBuilder
{
	/// <summary>Creates a builder over <paramref name="services"/>.</summary>
	public IdempotencyBuilder(IServiceCollection services)
	{
		Services = services ?? throw new ArgumentNullException(nameof(services));
	}

	/// <summary>Underlying service collection.</summary>
	public IServiceCollection Services { get; }

	/// <summary>
	/// Registers <see cref="RedisIdempotencyLock"/> as <see cref="IIdempotencyLock"/> using the host's
	/// <see cref="IConnectionMultiplexer"/>.
	/// </summary>
	public IdempotencyBuilder UseRedisLock()
	{
		Services.AddRedisIdempotencyLock();
		return this;
	}

	/// <summary>
	/// Registers optional <see cref="IdempotencyTelemetry"/> (ActivitySource).
	/// Hosts that export OTLP should <c>AddSource("BuildingBlocks.Idempotency")</c>.
	/// </summary>
	public IdempotencyBuilder UseTelemetry(Action<IdempotencyTelemetryOptions>? configure = null)
	{
		Services.AddIdempotencyTelemetry(configure);
		return this;
	}
}

/// <summary>DI registration for BuildingBlocks.Idempotency.</summary>
public static class IdempotencyServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="IdempotencyOptions"/>. Host must already register
	/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
	/// Chain <see cref="IdempotencyBuilder.UseRedisLock"/> when endpoints use <c>UseLock</c>,
	/// and <see cref="IdempotencyBuilder.UseTelemetry"/> for ActivitySource outcomes.
	/// </summary>
	/// <example>
	/// <code>
	/// services.AddBuildingBlocksIdempotency(o =&gt; o.UserIdFallback = "123")
	///     .UseRedisLock()
	///     .UseTelemetry();
	/// </code>
	/// </example>
	public static IdempotencyBuilder AddBuildingBlocksIdempotency(
		this IServiceCollection services,
		Action<IdempotencyOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		if (configure is not null)
			services.Configure<IdempotencyOptions>(configure);
		else
			services.AddOptions<IdempotencyOptions>();

		return new IdempotencyBuilder(services);
	}

	/// <summary>
	/// Registers <see cref="RedisIdempotencyLock"/> as <see cref="IIdempotencyLock"/> using the host's
	/// <see cref="IConnectionMultiplexer"/>. Does not register Redis cache or connection hosting.
	/// Prefer <see cref="IdempotencyBuilder.UseRedisLock"/> when chaining.
	/// </summary>
	public static IServiceCollection AddRedisIdempotencyLock(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAddSingleton<IIdempotencyLock>(sp =>
			new RedisIdempotencyLock(sp.GetRequiredService<IConnectionMultiplexer>()));

		return services;
	}

	/// <summary>
	/// Registers optional <see cref="IdempotencyTelemetry"/> (ActivitySource).
	/// Hosts that export OTLP should <c>AddSource("BuildingBlocks.Idempotency")</c> — no Telemetry package dependency.
	/// Prefer <see cref="IdempotencyBuilder.UseTelemetry"/> when chaining.
	/// </summary>
	public static IServiceCollection AddIdempotencyTelemetry(
		this IServiceCollection services,
		Action<IdempotencyTelemetryOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		var options = new IdempotencyTelemetryOptions();
		configure?.Invoke(options);
		services.TryAddSingleton(new IdempotencyTelemetry(options));
		return services;
	}
}
