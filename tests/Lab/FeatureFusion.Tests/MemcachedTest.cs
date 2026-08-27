using System;
using System.Threading.Tasks;
using Xunit;
using Enyim.Caching;
using Microsoft.Extensions.Logging;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Enyim.Caching.Configuration;
using FeatureFusion.Infrastructure.Caching;
namespace Tests.FeatureFusion.UnitTest
{
	public sealed class MemcachedCacheManagerTests : IClassFixture<MemcachedFixture>, IAsyncLifetime
	{
		private readonly MemcachedFixture _fixture;
		private MemcachedCacheManager _cacheManager = null!;

		public MemcachedCacheManagerTests(MemcachedFixture fixture)
		{
			_fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
		}

		public async Task InitializeAsync()
		{
			_cacheManager = _fixture.CreateCacheManager();
			await _fixture.ClearCacheAsync();
		}

		public Task DisposeAsync() => Task.CompletedTask;

		[Fact]
		public async Task ShouldRetrieveData_FromMemcached()
		{
			// Arrange
			var key = new CacheKey("test-key");
			var expectedValue = "hello-world";

			await _fixture.MemcachedClient.SetAsync(key.Key, expectedValue, TimeSpan.FromMinutes(1));

			// Act
			var result = await _cacheManager.GetValueOrCreateAsync(key, () => Task.FromResult("db-data"));

			// Assert
			Assert.Equal(expectedValue, result);
		}

		[Fact]
		public async Task ShouldFallbackToDB_WhenCacheMiss()
		{
			// Arrange
			var key = new CacheKey("missing-key");

			// Act
			var result = await _cacheManager.GetValueOrCreateAsync(key, () => Task.FromResult("db-data"));

			// Assert
			Assert.Equal("db-data", result);
		}
	}
}