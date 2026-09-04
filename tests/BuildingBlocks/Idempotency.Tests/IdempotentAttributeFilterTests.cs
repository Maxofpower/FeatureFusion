using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Idempotency;
using BuildingBlocks.Idempotency.AspNetCore;
using BuildingBlocks.Idempotency.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BuildingBlocks.Idempotency.Tests;

public sealed class IdempotentAttributeFilterTests
{
	private readonly IDistributedCache _distributedCache =
		new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

	private readonly Mock<IIdempotencyLock> _lockMock = new();
	private readonly IdempotencyOptions _options = new()
	{
		UserIdFallback = "123"
	};

	public IdempotentAttributeFilterTests()
	{
		_lockMock
			.Setup(l => l.AcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_lockMock
			.Setup(l => l.ReleaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
	}

	private HttpContext CreateHttpContext(
		string idempotencyKey,
		string? userId = "456",
		string? body = null,
		string method = "POST",
		string path = "/api/orders")
	{
		var httpContext = new DefaultHttpContext();
		httpContext.Request.Method = method;
		httpContext.Request.Path = path;
		httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
		if (userId is not null)
		{
			httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
			[
				new Claim(ClaimTypes.NameIdentifier, userId)
			]));
		}

		if (body is not null)
		{
			var bytes = Encoding.UTF8.GetBytes(body);
			httpContext.Request.Body = new MemoryStream(bytes);
			httpContext.Request.ContentLength = bytes.Length;
			httpContext.Request.ContentType = "application/json";
			httpContext.Request.EnableBuffering();
		}

		return httpContext;
	}

	private IdempotentAttributeFilter CreateFilter(
		bool useLock = false,
		IdempotencyOptions? options = null,
		IdempotencyTelemetry? telemetry = null,
		int processingTtlSeconds = 0,
		int entryTtlSeconds = 0)
	{
		return new IdempotentAttributeFilter(
			_distributedCache,
			NullLoggerFactory.Instance,
			useLock ? _lockMock.Object : null,
			options ?? _options,
			useLock,
			telemetry,
			processingTtlSeconds,
			entryTtlSeconds);
	}

	private static ActionExecutingContext CreateActionContext(HttpContext httpContext)
	{
		return new ActionExecutingContext(
			new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
			new List<IFilterMetadata>(),
			new Dictionary<string, object?>(),
			controller: new object());
	}

	private static ActionExecutedContext CreateSuccessfulActionExecutedContext(
		ActionExecutingContext context,
		int statusCode = 200)
	{
		return new ActionExecutedContext(context, new List<IFilterMetadata>(), null!)
		{
			Result = new ObjectResult(new { message = "Success" }) { StatusCode = statusCode }
		};
	}

	private static ProblemDetails AssertProblem(IActionResult? result, int status, string typeSuffix)
	{
		var objectResult = Assert.IsType<ObjectResult>(result);
		Assert.Equal(status, objectResult.StatusCode);
		var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
		Assert.Equal(status, problem.Status);
		Assert.Equal(IdempotencyProblemDetails.TypeBase + typeSuffix, problem.Type);
		return problem;
	}

	[Fact]
	public void ExtractAndValidateIdempotencyKey_ValidUlid_ReturnsKey()
	{
		var httpContext = CreateHttpContext(Ulid.NewUlid().ToString());
		var filter = CreateFilter();
		var result = filter.ExtractAndValidateIdempotencyKey(httpContext.Request);
		Assert.Equal(httpContext.Request.Headers["Idempotency-Key"], result);
	}

	[Fact]
	public void ExtractAndValidateIdempotencyKey_MissingHeader_ThrowsArgumentNullException()
	{
		var httpContext = new DefaultHttpContext();
		var filter = CreateFilter();
		var exception = Assert.Throws<ArgumentNullException>(() => filter.ExtractAndValidateIdempotencyKey(httpContext.Request));
		Assert.Equal("Idempotency-Key", exception.ParamName);
	}

	[Fact]
	public void ExtractAndValidateIdempotencyKey_EmptyHeader_ThrowsArgumentException()
	{
		var httpContext = CreateHttpContext(string.Empty);
		var filter = CreateFilter();
		var exception = Assert.Throws<ArgumentException>(() => filter.ExtractAndValidateIdempotencyKey(httpContext.Request));
		Assert.Equal("The Idempotency-Key value cannot be empty.", exception.Message);
	}

	[Fact]
	public void ExtractAndValidateIdempotencyKey_InvalidUlid_ThrowsArgumentException()
	{
		var httpContext = CreateHttpContext("invalid-ulid");
		var filter = CreateFilter();
		var exception = Assert.Throws<ArgumentException>(() => filter.ExtractAndValidateIdempotencyKey(httpContext.Request));
		Assert.Equal("Invalid Idempotency-Key format: invalid-ulid", exception.Message);
	}

	[Fact]
	public void KeyValidator_RejectsControlCharactersAndOverLength()
	{
		var options = new IdempotencyOptions { RequireUlid = false, MaxKeyLength = 8 };
		Assert.False(IdempotencyKeyValidator.TryValidate("ab\nc", options, out _));
		Assert.False(IdempotencyKeyValidator.TryValidate("123456789", options, out _));
		Assert.True(IdempotencyKeyValidator.TryValidate("ok-key", options, out _));
	}

	[Fact]
	public async Task FirstRequest_CachesEnvelope()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey);
		var filter = CreateFilter();
		var context = CreateActionContext(httpContext);
		var executedContext = CreateSuccessfulActionExecutedContext(context);

		await filter.OnActionExecutionAsync(context, () => Task.FromResult(executedContext));

		var cacheKey = $"Idempotency_456_{idempotencyKey}";
		var cachedData = await _distributedCache.GetAsync(cacheKey);
		Assert.NotNull(cachedData);

		var cachedEntry = JsonSerializer.Deserialize<IdempotencyCacheEntry>(Encoding.UTF8.GetString(cachedData));
		Assert.Equal("Completed", cachedEntry?.Status);
		Assert.Equal(200, cachedEntry?.StatusCode);
		Assert.Equal("application/json", cachedEntry?.ContentType);
		Assert.Equal("{\"message\":\"Success\"}", cachedEntry?.Response);
	}

	[Fact]
	public async Task Created201_IsCachedAndReplayed()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var filter = CreateFilter();
		var first = CreateActionContext(CreateHttpContext(idempotencyKey, "1"));
		await filter.OnActionExecutionAsync(first, () => Task.FromResult(CreateSuccessfulActionExecutedContext(first, 201)));

		var second = CreateActionContext(CreateHttpContext(idempotencyKey, "1"));
		var ran = false;
		await filter.OnActionExecutionAsync(second, () =>
		{
			ran = true;
			return Task.FromResult(CreateSuccessfulActionExecutedContext(second, 201));
		});

		Assert.False(ran);
		var content = Assert.IsType<ContentResult>(second.Result);
		Assert.Equal(201, content.StatusCode);
	}

	[Fact]
	public async Task DuplicateRequest_ReplaysStoredStatusAndContentType()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey, "1234");
		var cacheKey = $"Idempotency_1234_{idempotencyKey}";
		var cacheEntry = new IdempotencyCacheEntry
		{
			Status = "Completed",
			Response = "{\"message\":\"Cached\"}",
			StatusCode = 200,
			ContentType = "application/json"
		};
		await _distributedCache.SetAsync(cacheKey, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cacheEntry)));

		var filter = CreateFilter();
		var context = CreateActionContext(httpContext);
		await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), null!)));

		var contentResult = Assert.IsType<ContentResult>(context.Result);
		Assert.Equal("{\"message\":\"Cached\"}", contentResult.Content);
		Assert.Equal("application/json", contentResult.ContentType);
		Assert.Equal(200, contentResult.StatusCode);
		Assert.True(httpContext.Response.Headers.ContainsKey("X-Idempotent-Response"));
	}

	[Fact]
	public async Task DuplicateCompleted_ConflictStrategy_Returns409Problem()
	{
		var options = new IdempotencyOptions
		{
			UserIdFallback = "123",
			DuplicateCompletedBehavior = DuplicateCompletedBehavior.Conflict
		};
		var idempotencyKey = Ulid.NewUlid().ToString();
		var cacheKey = $"Idempotency_1_{idempotencyKey}";
		await _distributedCache.SetAsync(cacheKey, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new IdempotencyCacheEntry
		{
			Status = "Completed",
			Response = "{}",
			StatusCode = 200
		})));

		var filter = CreateFilter(options: options);
		var context = CreateActionContext(CreateHttpContext(idempotencyKey, "1"));
		await filter.OnActionExecutionAsync(context, () => Task.FromResult(CreateSuccessfulActionExecutedContext(context)));

		AssertProblem(context.Result, 409, "duplicate");
	}

	[Fact]
	public async Task ProcessingRequest_ReturnsConflictProblem()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey, "1234");
		var cacheKey = $"Idempotency_1234_{idempotencyKey}";
		var cacheEntry = new IdempotencyCacheEntry
		{
			Status = "Processing",
			ProcessingExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(1)
		};
		await _distributedCache.SetAsync(cacheKey, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cacheEntry)));

		var filter = CreateFilter();
		var context = CreateActionContext(httpContext);
		await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), null!)));

		var problem = AssertProblem(context.Result, 409, "processing");
		Assert.Equal("Request is already being processed. Please wait.", problem.Detail);
	}

	[Fact]
	public async Task ExpiredProcessing_AllowsNewExecution()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey, "999");
		var cacheKey = $"Idempotency_999_{idempotencyKey}";
		var stale = new IdempotencyCacheEntry
		{
			Status = "Processing",
			ProcessingExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
		};
		await _distributedCache.SetAsync(cacheKey, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(stale)));

		var filter = CreateFilter();
		var context = CreateActionContext(httpContext);
		var executed = CreateSuccessfulActionExecutedContext(context);
		var ran = false;

		await filter.OnActionExecutionAsync(context, () =>
		{
			ran = true;
			return Task.FromResult(executed);
		});

		Assert.True(ran);
		var cachedData = await _distributedCache.GetAsync(cacheKey);
		var entry = JsonSerializer.Deserialize<IdempotencyCacheEntry>(Encoding.UTF8.GetString(cachedData!));
		Assert.Equal("Completed", entry?.Status);
	}

	[Fact]
	public async Task OnActionExecutionAsync_FailedRequest_RemovesCacheEntry()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey, "2565");
		var filter = CreateFilter();
		var context = CreateActionContext(httpContext);

		async Task<ActionExecutedContext> Next()
		{
			await Task.Yield();
			throw new InvalidOperationException("Test exception");
		}

		await Assert.ThrowsAsync<InvalidOperationException>(() => filter.OnActionExecutionAsync(context, Next));
		Assert.Null(await _distributedCache.GetAsync($"Idempotency_2565_{idempotencyKey}"));
	}

	[Fact]
	public async Task FingerprintEnabled_SameBody_Replays()
	{
		var options = new IdempotencyOptions { UserIdFallback = "123", EnableRequestFingerprint = true };
		var body = """{"quantity":2}""";
		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey, "1", body);
		var filter = CreateFilter(options: options);
		var context = CreateActionContext(httpContext);
		await filter.OnActionExecutionAsync(context, () => Task.FromResult(CreateSuccessfulActionExecutedContext(context)));

		var httpContext2 = CreateHttpContext(idempotencyKey, "1", body);
		var context2 = CreateActionContext(httpContext2);
		var ran = false;
		await filter.OnActionExecutionAsync(context2, () =>
		{
			ran = true;
			return Task.FromResult(CreateSuccessfulActionExecutedContext(context2));
		});

		Assert.False(ran);
		Assert.IsType<ContentResult>(context2.Result);
		Assert.True(httpContext2.Response.Headers.ContainsKey("X-Idempotent-Response"));
	}

	[Fact]
	public async Task FingerprintEnabled_DifferentBody_Returns422Problem()
	{
		var options = new IdempotencyOptions { UserIdFallback = "123", EnableRequestFingerprint = true };
		var idempotencyKey = Ulid.NewUlid().ToString();
		var filter = CreateFilter(options: options);

		var first = CreateActionContext(CreateHttpContext(idempotencyKey, "1", """{"quantity":2}"""));
		await filter.OnActionExecutionAsync(first, () => Task.FromResult(CreateSuccessfulActionExecutedContext(first)));

		var second = CreateActionContext(CreateHttpContext(idempotencyKey, "1", """{"quantity":5}"""));
		await filter.OnActionExecutionAsync(second, () => Task.FromResult(CreateSuccessfulActionExecutedContext(second)));

		AssertProblem(second.Result, 422, "fingerprint-mismatch");
	}

	[Fact]
	public async Task FingerprintEnabled_DifferentPath_Returns422Problem()
	{
		var options = new IdempotencyOptions { UserIdFallback = "123", EnableRequestFingerprint = true };
		var idempotencyKey = Ulid.NewUlid().ToString();
		var body = """{"quantity":2}""";
		var filter = CreateFilter(options: options);

		var first = CreateActionContext(CreateHttpContext(idempotencyKey, "1", body, path: "/api/a"));
		await filter.OnActionExecutionAsync(first, () => Task.FromResult(CreateSuccessfulActionExecutedContext(first)));

		var second = CreateActionContext(CreateHttpContext(idempotencyKey, "1", body, path: "/api/b"));
		await filter.OnActionExecutionAsync(second, () => Task.FromResult(CreateSuccessfulActionExecutedContext(second)));

		AssertProblem(second.Result, 422, "fingerprint-mismatch");
	}

	[Fact]
	public async Task FingerprintDisabled_DifferentBody_ReplaysOriginal()
	{
		var options = new IdempotencyOptions { UserIdFallback = "123", EnableRequestFingerprint = false };
		var idempotencyKey = Ulid.NewUlid().ToString();
		var filter = CreateFilter(options: options);

		var first = CreateActionContext(CreateHttpContext(idempotencyKey, "1", """{"quantity":2}"""));
		await filter.OnActionExecutionAsync(first, () => Task.FromResult(CreateSuccessfulActionExecutedContext(first)));

		var second = CreateActionContext(CreateHttpContext(idempotencyKey, "1", """{"quantity":5}"""));
		var ran = false;
		await filter.OnActionExecutionAsync(second, () =>
		{
			ran = true;
			return Task.FromResult(CreateSuccessfulActionExecutedContext(second));
		});

		Assert.False(ran);
		Assert.IsType<ContentResult>(second.Result);
	}

	[Fact]
	public async Task KeyScopeClaim_IsIncludedInCacheKey()
	{
		var options = new IdempotencyOptions { UserIdFallback = "123" };
		options.KeyScopeClaimTypes.Add("tenant_id");

		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey, "456");
		httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new Claim(ClaimTypes.NameIdentifier, "456"),
			new Claim("tenant_id", "acme")
		]));

		var filter = CreateFilter(options: options);
		var context = CreateActionContext(httpContext);
		await filter.OnActionExecutionAsync(context, () => Task.FromResult(CreateSuccessfulActionExecutedContext(context)));

		Assert.NotNull(await _distributedCache.GetAsync($"Idempotency_acme_456_{idempotencyKey}"));
		Assert.Null(await _distributedCache.GetAsync($"Idempotency_456_{idempotencyKey}"));
	}

	[Fact]
	public async Task PerEndpointProcessingTtl_IsApplied()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var filter = CreateFilter(processingTtlSeconds: 30);
		var context = CreateActionContext(CreateHttpContext(idempotencyKey, "7"));

		var gateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var run = filter.OnActionExecutionAsync(context, async () =>
		{
			gateEntered.SetResult();
			await allowComplete.Task;
			return CreateSuccessfulActionExecutedContext(context);
		});

		await gateEntered.Task;
		var cacheKey = $"Idempotency_7_{idempotencyKey}";
		var raw = await _distributedCache.GetAsync(cacheKey);
		Assert.NotNull(raw);
		var entry = JsonSerializer.Deserialize<IdempotencyCacheEntry>(Encoding.UTF8.GetString(raw));
		Assert.NotNull(entry?.ProcessingExpiresAtUtc);
		var remaining = entry!.ProcessingExpiresAtUtc!.Value - DateTimeOffset.UtcNow;
		Assert.True(remaining <= TimeSpan.FromSeconds(30) + TimeSpan.FromSeconds(2));
		Assert.True(remaining > TimeSpan.FromSeconds(20));

		allowComplete.SetResult();
		await run;
	}

	[Fact]
	public async Task Telemetry_RecordsExecutedAndReplayed_WithoutCacheKeyByDefault()
	{
		var outcomes = new List<string>();
		var sawCacheKey = false;
		using var listener = new ActivityListener
		{
			ShouldListenTo = s => s.Name == "BuildingBlocks.Idempotency",
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
			ActivityStopped = a =>
			{
				var o = a.GetTagItem("idempotency.outcome")?.ToString();
				if (o is not null)
					outcomes.Add(o);
				if (a.GetTagItem("idempotency.cache_key") is not null)
					sawCacheKey = true;
			}
		};
		ActivitySource.AddActivityListener(listener);

		var telemetry = new IdempotencyTelemetry(new IdempotencyTelemetryOptions());
		var idempotencyKey = Ulid.NewUlid().ToString();
		var filter = CreateFilter(telemetry: telemetry);

		var first = CreateActionContext(CreateHttpContext(idempotencyKey));
		await filter.OnActionExecutionAsync(first, () => Task.FromResult(CreateSuccessfulActionExecutedContext(first)));

		var second = CreateActionContext(CreateHttpContext(idempotencyKey));
		await filter.OnActionExecutionAsync(second, () => Task.FromResult(CreateSuccessfulActionExecutedContext(second)));

		Assert.Contains(IdempotencyOutcomes.Executed, outcomes);
		Assert.Contains(IdempotencyOutcomes.Replayed, outcomes);
		Assert.False(sawCacheKey);
	}

	[Fact]
	public async Task UseLockTrue_AcquiresAndReleasesLock()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey);
		var filter = CreateFilter(useLock: true);
		var context = CreateActionContext(httpContext);
		var lockKey = $"Idempotency_456_{idempotencyKey}_lock";

		await filter.OnActionExecutionAsync(context, () => Task.FromResult(CreateSuccessfulActionExecutedContext(context)));

		_lockMock.Verify(
			l => l.AcquireAsync(lockKey, It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
			Times.Once);
		_lockMock.Verify(
			l => l.ReleaseAsync(lockKey, It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public async Task UseLockTrue_FailsToAcquireLock_Returns500Problem()
	{
		var idempotencyKey = Ulid.NewUlid().ToString();
		var httpContext = CreateHttpContext(idempotencyKey, "466");
		_lockMock
			.Setup(l => l.AcquireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);

		var filter = CreateFilter(useLock: true);
		var context = CreateActionContext(httpContext);
		await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), null!)));

		AssertProblem(context.Result, 500, "lock-failure");
	}

	[Fact]
	public async Task MissingUserClaim_WithoutFallback_Returns401Problem()
	{
		var options = new IdempotencyOptions { UserIdFallback = null };
		var httpContext = CreateHttpContext(Ulid.NewUlid().ToString(), userId: null);
		var filter = CreateFilter(options: options);
		var context = CreateActionContext(httpContext);
		await filter.OnActionExecutionAsync(context, () => Task.FromResult(CreateSuccessfulActionExecutedContext(context)));
		AssertProblem(context.Result, 401, "unauthorized");
	}

	[Fact]
	public async Task MinimalApiFilter_MissThenReplay()
	{
		var options = new IdempotencyOptions { UserIdFallback = "123" };
		var endpoint = IdempotencyEndpointSettings.Create(useLock: false);
		var filter = new IdempotentEndpointFilter(
			_distributedCache,
			NullLoggerFactory.Instance,
			options,
			endpoint);

		var key = Ulid.NewUlid().ToString();
		var http1 = CreateHttpContext(key, "9");
		var ctx1 = new DefaultEndpointFilterInvocationContext(http1);
		var result1 = await filter.InvokeAsync(ctx1, _ => new ValueTask<object?>(Results.Ok(new { message = "ok" })));
		Assert.NotNull(result1);

		var http2 = CreateHttpContext(key, "9");
		var ctx2 = new DefaultEndpointFilterInvocationContext(http2);
		var ran = false;
		var result2 = await filter.InvokeAsync(ctx2, _ =>
		{
			ran = true;
			return new ValueTask<object?>(Results.Ok(new { message = "again" }));
		});

		Assert.False(ran);
		Assert.True(http2.Response.Headers.ContainsKey("X-Idempotent-Response"));
		Assert.NotNull(result2);
	}

	[Fact]
	public void FluentDi_RegistersOptionsLockAndTelemetry()
	{
		var services = new ServiceCollection();
		services.AddBuildingBlocksIdempotency(o => o.UserIdFallback = "x")
			.UseTelemetry(t => t.IncludeCacheKeyInTelemetry = true);

		var sp = services.BuildServiceProvider();
		Assert.Equal("x", sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value.UserIdFallback);
		Assert.NotNull(sp.GetService<IdempotencyTelemetry>());
	}

	[Fact]
	public void TryCaptureSuccessfulResponse_Accepts2xx()
	{
		Assert.True(IdempotentAttributeFilter.TryCaptureSuccessfulResponse(
			new ObjectResult(new { a = 1 }) { StatusCode = 201 },
			out var code,
			out _,
			out _));
		Assert.Equal(201, code);

		Assert.True(IdempotentAttributeFilter.TryCaptureSuccessfulResponse(
			new StatusCodeResult(204),
			out code,
			out _,
			out _));
		Assert.Equal(204, code);

		Assert.False(IdempotentAttributeFilter.TryCaptureSuccessfulResponse(
			new ObjectResult("err") { StatusCode = 400 },
			out _,
			out _,
			out _));
	}
}

internal sealed class DefaultEndpointFilterInvocationContext : EndpointFilterInvocationContext
{
	public DefaultEndpointFilterInvocationContext(HttpContext httpContext)
	{
		HttpContext = httpContext;
	}

	public override HttpContext HttpContext { get; }

	public override IList<object?> Arguments { get; } = new List<object?>();

	public override T GetArgument<T>(int index) => (T)Arguments[index]!;
}
