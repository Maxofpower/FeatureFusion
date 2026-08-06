namespace FeatureFusion.Infrastructure.Filters
{
	using FeatureFusion.Infrastructure.Caching;
	using Microsoft.AspNetCore.Mvc.Filters;
	using Microsoft.Extensions.Caching.Distributed;
	using Microsoft.Extensions.Caching.Hybrid;

	/// <summary>
	/// IdempotentFusion: applies ULID <c>Idempotency-Key</c> handling via Redis-backed status tracking (optional lock).
	/// </summary>
	/// <remarks>
	/// LinkedIn (IdentifiedCommand / MediatR pattern):
	/// https://www.linkedin.com/feed/update/urn:li:activity:7303686809891356676/
	/// LinkedIn (IdempotentFusion in this repo):
	/// https://www.linkedin.com/feed/update/urn:li:activity:7309149985307029504/
	/// Catalog: docs/linkedin-posts.md (<c>idempotency-mediatr</c>, <c>idempotentfusion</c>).
	/// </remarks>
	[AttributeUsage(AttributeTargets.Method)]
	public class IdempotentAttribute : Attribute, IFilterFactory
	{
		public bool UseLock { get; set; }

		public IdempotentAttribute(bool useLock = false)
		{
			UseLock = useLock;
		}
		public bool IsReusable => false; // Filters are not reusable

		public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
		{
			var distributedCache = serviceProvider.GetService<IDistributedCache>();
			var redisWrapper = serviceProvider.GetService<IRedisConnectionWrapper>();
			var loggerFactory = (ILoggerFactory)serviceProvider.GetService(typeof(ILoggerFactory));

			return new IdempotentAttributeFilter(
				distributedCache,
				loggerFactory,
				redisWrapper,
				useLock: UseLock);		
		}
	}
}
