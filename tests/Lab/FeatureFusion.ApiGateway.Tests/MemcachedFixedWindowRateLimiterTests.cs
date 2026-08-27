using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Enyim.Caching;
using Microsoft.Extensions.Caching.Memory;
using FeatureFusion.ApiGateway.RateLimiter;
using System.Threading.RateLimiting;
namespace Tests.FeatureFusion.ApiGateway
{
	public sealed class MemcachedFixedWindowRateLimiterTests : IClassFixture<MemcachedFixture>, IAsyncLifetime
	{
		private readonly MemcachedFixture _fixture;
		private MemcachedFixedWindowRateLimiter<string> _rateLimiter = null!;

		public MemcachedFixedWindowRateLimiterTests(MemcachedFixture fixture)
		{
			_fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
		}

		public async Task InitializeAsync()
		{
			var options = new MemcachedFixedWindowRateLimiterOptions
			{
				PermitLimit = 10, // Allow 10 requests per minute
				Window = TimeSpan.FromMinutes(1) // 1-minute window
			};

			_rateLimiter = new MemcachedFixedWindowRateLimiter<string>(
				partitionKey: "test-partition",
				options: options,
				memcachedClient: _fixture.MemcachedClient,
				memoryCache: new MemoryCache(new MemoryCacheOptions())
			);

			await _fixture.ClearCacheAsync();
		}

		public Task DisposeAsync() => Task.CompletedTask;

		[Fact]
		public async Task ShouldAllowRequests_WithinLimit()
		{
			// Act
			for (int i = 0; i < 10; i++)
			{
				var lease = await _rateLimiter.AcquireAsync(1, CancellationToken.None);
				Assert.True(lease.IsAcquired, $"Request {i + 1} should be allowed.");
			}
		}

		[Fact]
		public async Task ShouldDenyRequests_ExceedingLimit()
		{
			// Act
			for (int i = 0; i < 10; i++)
			{
				var lease = await _rateLimiter.AcquireAsync(1, CancellationToken.None);
				Assert.True(lease.IsAcquired, $"Request {i + 1} should be allowed.");
			}

			// Exceed the limit
			var lease11 = await _rateLimiter.AcquireAsync(1, CancellationToken.None);
			Assert.False(lease11.IsAcquired, "Request 11 should be denied.");
		}

		[Fact]
		public async Task ShouldFallbackToInMemoryCache_WhenMemcachedFails()
		{
			// Arrange: Simulate Memcached failure by disposing the client.
			_fixture.MemcachedClient.Dispose();

			// Act
			var lease = await _rateLimiter.AcquireAsync(1, CancellationToken.None);

			// Assert
			Assert.True(lease.IsAcquired, "Request should be allowed (fallback to in-memory cache).");
		}

		[Fact]
		public async Task ShouldAllowRequests_WhenBothMemcachedAndInMemoryCacheFail()
		{
			// Arrange: Simulate Memcached failure by disposing the client.
			_fixture.MemcachedClient.Dispose();

			// Simulate in-memory cache failure by clearing it.
			var memoryCacheField = typeof(MemcachedFixedWindowRateLimiter<string>)
				.GetField("_memoryCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			var memoryCache = (MemoryCache)memoryCacheField!.GetValue(_rateLimiter)!;
			memoryCache.Dispose(); // Dispose the cache to clear it.

			// Act
			var lease = await _rateLimiter.AcquireAsync(1, CancellationToken.None);

			// Assert
			Assert.True(lease.IsAcquired, "Request should be allowed (fail-open behavior).");
		}

		[Fact]
		public async Task ShouldHandleConcurrentRequests_Correctly()
		{
			// Arrange
			var tasks = new List<Task>();

			// Act
			for (int i = 1; i <= 50; i++)
			{
				tasks.Add(Task.Run(async () =>
				{
					var lease = await _rateLimiter.AcquireAsync(1, CancellationToken.None);
					if (lease.IsAcquired)
					{

						if (lease.TryGetMetadata("RATELIMIT_REMAINING", out var remaining))
							Console.WriteLine($"Request allowed: Count = {remaining?.ToString()}");
					}
					else
					{
						if (lease.TryGetMetadata("RATELIMIT_RETRYAFTER", out var retryAfter))
						{
							Console.WriteLine($"Request denied: RetryAfter = {retryAfter?.ToString()}");
						}

					}
				}));
			}

			await Task.WhenAll(tasks);

			// Assert
			// Ensure that no more than 10 requests are allowed within the limit.
			var lease16 = await _rateLimiter.AcquireAsync(1, CancellationToken.None);
			Assert.False(lease16.IsAcquired, "Request 51 should be denied.");
		}

		[Fact]
		public async Task ShouldResetCount_AfterWindowExpires()
		{
			// Arrange
			var options = new MemcachedFixedWindowRateLimiterOptions
			{
				PermitLimit = 10, // Allow 10 requests per minute
				Window = TimeSpan.FromSeconds(10) // 10-second window for faster testing
			};

			_rateLimiter = new MemcachedFixedWindowRateLimiter<string>(
				partitionKey: "test-partition",
				options: options,
				memcachedClient: _fixture.MemcachedClient,
				memoryCache: new MemoryCache(new MemoryCacheOptions())
			);

			// Act: Exhaust the limit.
			for (int i = 1; i <= 10; i++)
			{
				var lease = await _rateLimiter.AcquireAsync(1, CancellationToken.None);
				Assert.True(lease.IsAcquired, $"Request {i} should be allowed.");
			}

			// Wait for the window to expire.
			await Task.Delay(TimeSpan.FromSeconds(10));

			// Act: Try again after the window has expired.
			var lease11 = await _rateLimiter.AcquireAsync(1, CancellationToken.None);

			// Assert
			Assert.True(lease11.IsAcquired, "Request 11 should be allowed after the window expires.");
		}

		[Fact]
		public async Task ShouldHandleMultiplePermits_Correctly()
		{
			// Arrange
			var options = new MemcachedFixedWindowRateLimiterOptions
			{
				PermitLimit = 10, // Allow 10 requests per minute
				Window = TimeSpan.FromMinutes(1) // 1-minute window
			};

			_rateLimiter = new MemcachedFixedWindowRateLimiter<string>(
				partitionKey: "test-partition",
				options: options,
				memcachedClient: _fixture.MemcachedClient,
				memoryCache: new MemoryCache(new MemoryCacheOptions())
			);

			// Act: Request 5 permits (half the limit).
			var lease = await _rateLimiter.AcquireAsync(5, CancellationToken.None);

			// Assert
			Assert.True(lease.IsAcquired, "Request for 5 permits should be allowed.");

			// Act: Request another 5 permits (reaching the limit).
			var lease2 = await _rateLimiter.AcquireAsync(5, CancellationToken.None);

			// Assert
			Assert.True(lease2.IsAcquired, "Request for another 5 permits should be allowed.");

			// Act: Request 1 more permit (exceeding the limit).
			var lease3 = await _rateLimiter.AcquireAsync(1, CancellationToken.None);

			// Assert
			Assert.False(lease3.IsAcquired, "Request for 1 more permit should be denied.");
		}
		[Fact]
		public async Task FallbackToInMemoryCache_WhenMemcachedFails()
		{
			// Arrange: Simulate Memcached failure by disposing the client.
			_fixture.MemcachedClient.Dispose();

			// Act: Make requests to the rate limiter.
			var tasks = new List<Task>();
			for (int i = 0; i < 10; i++)
			{
				tasks.Add(Task.Run(async () =>
				{
					var lease = await _rateLimiter.AcquireAsync(1, CancellationToken.None);
					Assert.True(lease.IsAcquired, $"Request {i + 1} should be allowed (fallback to in-memory cache).");
				}));
			}

			await Task.WhenAll(tasks);

			// Assert: Ensure that the in-memory cache is used.
			var lease11 = await _rateLimiter.AcquireAsync(1, CancellationToken.None);
			Assert.False(lease11.IsAcquired, "Request 11 should be denied (in-memory cache limit reached).");
		}
	}
}
