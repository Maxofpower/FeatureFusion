using Castle.Core.Logging;
using FeatureFusion.Controllers.V2;
using FeatureFusion.Features.Orders.Commands;
using FeatureFusion.Infrastructure.Caching;
using BuildingBlocks.Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Testcontainers.Redis;
using Xunit;

namespace Tests.FeatureFusion.IdempotentAttributeFilterTests
{
	public class IdempotentAttributeFilterTests : IAsyncLifetime
	{
		private readonly RedisContainer _redisContainer = new RedisBuilder().Build();
		private IDistributedCache? _distributedCache;
		private Mock<IRedisConnectionWrapper>? _redisConnectionWrapperMock;

		public async Task InitializeAsync()
		{
			await _redisContainer.StartAsync();

			var services = new ServiceCollection();
			services.AddStackExchangeRedisCache(options =>
			{
				options.Configuration = _redisContainer.GetConnectionString();
			});

			var serviceProvider = services.BuildServiceProvider();
			_distributedCache = serviceProvider.GetRequiredService<IDistributedCache>();

			_redisConnectionWrapperMock = new Mock<IRedisConnectionWrapper>();
		}

		public async Task DisposeAsync()
		{
			await _redisContainer.DisposeAsync();
		}

		// Helper method to create an HttpContext with an Idempotency-Key header and user claims
		private HttpContext CreateHttpContext(string idempotencyKey, string userId = "456")
		{
			var httpContext = new DefaultHttpContext();
			httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
			httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }));
			return httpContext;
		}

		// Helper method to create an IdempotentAttributeFilter instance
		private IdempotentAttributeFilter CreateFilter(bool useLock = false)
		{
			return new IdempotentAttributeFilter(_distributedCache, new NullLoggerFactory(), _redisConnectionWrapperMock?.Object, useLock);
		}

		// Helper method to create an ActionExecutingContext
		private ActionExecutingContext CreateActionContext(HttpContext httpContext, object controller)
		{
			return new ActionExecutingContext(
				new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
				new List<IFilterMetadata>(),
				new Dictionary<string, object?>(),
				controller);
		}

		// Helper method to create a mock OrderController
		private OrderController CreateMockOrderController()
		{
			var mockLogger = new Mock<ILogger<OrderRequestValidator>>();
			var validator = new OrderRequestValidator(mockLogger.Object);
			var mockMediator = new Mock<IMediator>();
			return new OrderController(validator, mockMediator.Object);
		}

		// Helper method to create a successful ActionExecutedContext
		private ActionExecutedContext CreateSuccessfulActionExecutedContext(ActionExecutingContext context)
		{
			return new ActionExecutedContext(context, new List<IFilterMetadata>(), new Exception("No exception occurred"))
			{
				Result = new ObjectResult(new { message = "Success" }) { StatusCode = 200 }
			};
		}

		[Fact]
		public void ExtractAndValidateIdempotencyKey_ValidUlid_ReturnsUlid()
		{
			// Arrange
			var httpContext = CreateHttpContext(Ulid.NewUlid().ToString());
			var filter = CreateFilter();

			// Act
			var result = filter.ExtractAndValidateIdempotencyKey(httpContext.Request);

			// Assert
			Assert.Equal(httpContext.Request.Headers["Idempotency-Key"], result.ToString());
		}

		[Fact]
		public void ExtractAndValidateIdempotencyKey_MissingHeader_ThrowsArgumentNullException()
		{
			// Arrange
			var httpContext = new DefaultHttpContext();
			var filter = CreateFilter();

			// Act & Assert
			var exception = Assert.Throws<ArgumentNullException>(() => filter.ExtractAndValidateIdempotencyKey(httpContext.Request));
			Assert.Equal("Idempotency-Key", exception.ParamName);
			Assert.Contains("The Idempotency-Key header is missing.", exception.Message);
		}

		[Fact]
		public void ExtractAndValidateIdempotencyKey_EmptyHeader_ThrowsArgumentException()
		{
			// Arrange
			var httpContext = CreateHttpContext(string.Empty);
			var filter = CreateFilter();

			// Act & Assert
			var exception = Assert.Throws<ArgumentException>(() => filter.ExtractAndValidateIdempotencyKey(httpContext.Request));
			Assert.Equal("The Idempotency-Key value cannot be empty.", exception.Message);
		}

		[Fact]
		public void ExtractAndValidateIdempotencyKey_InvalidUlid_ThrowsArgumentException()
		{
			// Arrange
			var httpContext = CreateHttpContext("invalid-ulid");
			var filter = CreateFilter();

			// Act & Assert
			var exception = Assert.Throws<ArgumentException>(() => filter.ExtractAndValidateIdempotencyKey(httpContext.Request));
			Assert.Equal("Invalid Idempotency-Key format: invalid-ulid", exception.Message);
		}

		[Fact]
		public async Task FirstRequest_CachesResponse()
		{
			// Arrange
			var idempotencyKey = Ulid.NewUlid().ToString();
			var httpContext = CreateHttpContext(idempotencyKey);
			var filter = CreateFilter();
			var context = CreateActionContext(httpContext, CreateMockOrderController());
			var executedContext = CreateSuccessfulActionExecutedContext(context);

			// Act
			await filter.OnActionExecutionAsync(context, () => Task.FromResult(executedContext));

			// Assert
			var cacheKey = $"Idempotency_456_{idempotencyKey}";
			var cachedData = await _distributedCache!.GetAsync(cacheKey);
			Assert.NotNull(cachedData);

			var cachedEntry = JsonConvert.DeserializeObject<IdempotencyCacheEntry>(Encoding.UTF8.GetString(cachedData));
			Assert.Equal("Completed", cachedEntry?.Status);
			Assert.Equal("{\"message\":\"Success\"}", cachedEntry?.Response);
		}

		[Fact]
		public async Task DuplicateRequest_ReturnsCachedResponse()
		{
			// Arrange
			var idempotencyKey = Ulid.NewUlid().ToString();
			var httpContext = CreateHttpContext(idempotencyKey, "1234");
			var cacheKey = $"Idempotency_1234_{idempotencyKey}";
			var cacheEntry = new IdempotencyCacheEntry { Status = "Completed", Response = "{\"message\":\"Cached\"}" };
			await _distributedCache!.SetAsync(cacheKey, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(cacheEntry)));

			var filter = CreateFilter();
			var context = CreateActionContext(httpContext, CreateMockOrderController());

			// Act
			await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), null!)));

			// Assert
			Assert.IsType<ContentResult>(context.Result);
			var contentResult = context.Result as ContentResult;
			Assert.Equal("{\"message\":\"Cached\"}", contentResult?.Content);
			Assert.Equal("application/json", contentResult?.ContentType);
			Assert.Equal(200, contentResult?.StatusCode);
			Assert.True(httpContext.Response.Headers.ContainsKey("X-Idempotent-Response"));
		}

		[Fact]
		public async Task ProcessingRequest_ReturnsConflict()
		{
			// Arrange
			var idempotencyKey = Ulid.NewUlid().ToString();
			var httpContext = CreateHttpContext(idempotencyKey, "1234");
			var cacheKey = $"Idempotency_1234_{idempotencyKey}";
			var cacheEntry = new IdempotencyCacheEntry { Status = "Processing" };
			await _distributedCache!.SetAsync(cacheKey, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(cacheEntry)));

			var filter = CreateFilter();
			var context = CreateActionContext(httpContext, CreateMockOrderController());

			// Act
			await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), null!)));

			// Assert
			Assert.IsType<ConflictObjectResult>(context.Result);
			var conflictResult = context.Result as ConflictObjectResult;
			Assert.Equal("Request is already being processed. Please wait.", conflictResult?.Value);
		}

		[Fact]
		public async Task OnActionExecutionAsync_FailedRequest_RemovesCacheEntry()
		{
			// Arrange
			var idempotencyKey = Ulid.NewUlid().ToString();
			var httpContext = CreateHttpContext(idempotencyKey, "2565");
			var filter = CreateFilter();
			var context = CreateActionContext(httpContext, CreateMockOrderController());

			// Simulate a failed action execution
			async Task<ActionExecutedContext> Next()
			{
				await Task.Yield();
				throw new Exception("Test exception");
				
			}

			// Act
			await filter.OnActionExecutionAsync(context, Next);

			// Assert
			var cacheKey = $"Idempotency_2565_{idempotencyKey}";
			var cachedData = await _distributedCache!.GetAsync(cacheKey);
			Assert.Null(cachedData);
		}

		[Fact]
		public async Task UseLockTrue_AcquiresLock()
		{
			// Arrange
			var idempotencyKey = Ulid.NewUlid().ToString();
			var httpContext = CreateHttpContext(idempotencyKey);
			var filter = CreateFilter(useLock: true);
			var context = CreateActionContext(httpContext, CreateMockOrderController());
			var executedContext = CreateSuccessfulActionExecutedContext(context);

			// Act
			await filter.OnActionExecutionAsync(context, () => Task.FromResult(executedContext));

			// Assert
			_redisConnectionWrapperMock?.Verify(r => r.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
		}

		[Fact]
		public async Task UseLockTrue_ReleasesLock()
		{
			// Arrange
			var idempotencyKey = Ulid.NewUlid().ToString();
			var httpContext = CreateHttpContext(idempotencyKey);
			var filter = CreateFilter(useLock: true);
			var context = CreateActionContext(httpContext, CreateMockOrderController());
			var executedContext = CreateSuccessfulActionExecutedContext(context);


			var lockKey = $"Idempotency_456_{idempotencyKey}_lock";
			var lockValue = Guid.NewGuid().ToString();

			// Mock the Redis connection wrapper to simulate successful lock acquisition and release
			_redisConnectionWrapperMock?
				.Setup(r => r.AcquireLockAsync(lockKey, It.IsAny<string>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(true);

			_redisConnectionWrapperMock?
				.Setup(r => r.ReleaseLockAsync(lockKey, It.IsAny<string>()))
				.Returns(Task.FromResult(true));

			// Act
			await filter.OnActionExecutionAsync(context, () => Task.FromResult(executedContext));

			// Assert
			_redisConnectionWrapperMock?.Verify(r => r.ReleaseLockAsync(lockKey, It.IsAny<string>()), Times.Once);
		}

		[Fact]
		public async Task UseLockTrue_FailsToAcquireLock_Returns500()
		{
			// Arrange
			var idempotencyKey = Ulid.NewUlid().ToString();
			var httpContext = CreateHttpContext(idempotencyKey, "466");
			var redisConnectionWrapper = new RedisConnectionWrapper(Options.Create(new RedisCacheOptions
			{
				Configuration = _redisContainer.GetConnectionString()
			}));

			var lockKey = $"Idempotency_466_{idempotencyKey}_lock";
			await redisConnectionWrapper.AcquireLockAsync(lockKey, "another-process-lock-value", TimeSpan.FromSeconds(10));

			var filter = new IdempotentAttributeFilter(_distributedCache, new NullLoggerFactory(), redisConnectionWrapper, true);
			var context = CreateActionContext(httpContext, CreateMockOrderController());

			// Act
			await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), null!)));

			// Assert
			Assert.IsType<StatusCodeResult>(context.Result);
			var statusCodeResult = context.Result as StatusCodeResult;
			Assert.Equal(StatusCodes.Status500InternalServerError, statusCodeResult?.StatusCode);

			// Clean up
			await redisConnectionWrapper.ReleaseLockAsync(lockKey, "another-process-lock-value");
		}
	}
}