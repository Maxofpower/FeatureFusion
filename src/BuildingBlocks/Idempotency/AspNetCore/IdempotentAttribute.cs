using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Idempotency.AspNetCore;

/// <summary>
/// Applies <c>Idempotency-Key</c> handling via <see cref="IDistributedCache"/> status tracking
/// and optional <see cref="IIdempotencyLock"/>.
/// </summary>
/// <remarks>
/// Defaults stay Exp 3 / 4 / 12 compatible: replay Completed, Processing → 409, fingerprint off,
/// optional Redis lock when <see cref="UseLock"/> is true.
/// Per-endpoint TTL: set <see cref="ProcessingTtlSeconds"/> / <see cref="EntryTtlSeconds"/> to a
/// positive value to override global <see cref="IdempotencyOptions"/> (<c>0</c> = use global).
/// </remarks>
/// <example>
/// <code>
/// [HttpPost]
/// [Idempotent(useLock: true)]
/// public async Task&lt;ActionResult&gt; Create(...) { ... }
/// </code>
/// </example>
/// <seealso cref="IdempotentEndpointFilter"/>
/// <seealso cref="IdempotencyEndpointRouteBuilderExtensions.WithIdempotency"/>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
	/// <summary>When true, GetOrCreate is wrapped in <see cref="IIdempotencyLock"/>.</summary>
	public bool UseLock { get; set; }

	/// <summary>
	/// Optional Processing TTL in seconds for this endpoint. <c>0</c> (default) uses global options.
	/// </summary>
	public int ProcessingTtlSeconds { get; set; }

	/// <summary>
	/// Optional Completed entry TTL in seconds for this endpoint. <c>0</c> (default) uses global options.
	/// </summary>
	public int EntryTtlSeconds { get; set; }

	/// <summary>Creates the attribute.</summary>
	public IdempotentAttribute(bool useLock = false)
	{
		UseLock = useLock;
	}

	/// <inheritdoc />
	public bool IsReusable => false;

	/// <inheritdoc />
	public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		var distributedCache = serviceProvider.GetRequiredService<IDistributedCache>();
		var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
		var options = serviceProvider.GetService<IOptions<IdempotencyOptions>>()?.Value
			?? new IdempotencyOptions();
		var idempotencyLock = serviceProvider.GetService<IIdempotencyLock>();
		var telemetry = serviceProvider.GetService<IdempotencyTelemetry>();

		return new IdempotentAttributeFilter(
			distributedCache,
			loggerFactory,
			idempotencyLock,
			options,
			useLock: UseLock,
			telemetry,
			ProcessingTtlSeconds,
			EntryTtlSeconds);
	}
}
