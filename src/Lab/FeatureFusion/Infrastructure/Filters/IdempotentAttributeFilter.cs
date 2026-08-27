using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using static System.Ulid;
using FeatureFusion.Infrastructure.Caching;
using System.Text;

public sealed class IdempotentAttributeFilter : IAsyncResourceFilter, IAsyncActionFilter
{
	private readonly IDistributedCache _distributedCache;
	private readonly ILogger<IdempotentAttributeFilter> _logger;
	private readonly IRedisConnectionWrapper _redisConnectionWrapper;
	private readonly bool _useLock;

	public IdempotentAttributeFilter(
		IDistributedCache distributedCache,
		ILoggerFactory loggerFactory,
		IRedisConnectionWrapper redisConnectionWrapper,
		bool useLock)
	{
		_distributedCache = distributedCache;
		_logger = loggerFactory.CreateLogger<IdempotentAttributeFilter>();
		_redisConnectionWrapper = redisConnectionWrapper;
		_useLock = useLock;
	}

	// Early validation (before model binding)
	public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
	{
		try
		{
			ExtractAndValidateIdempotencyKey(context.HttpContext.Request);
			await next();
		}
		catch (ArgumentNullException ex)
		{
			context.Result = new BadRequestObjectResult(ex.Message);
		}
		catch (ArgumentException ex)
		{
			context.Result = new BadRequestObjectResult(ex.Message);
		}
	}
	// Main idempotency logic (after model binding)
	public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
	{
		string cacheKey = null;

		try
		{
			// Extract and validate the Idempotency-Key header as a ULID
			var idempotencyKey = ExtractAndValidateIdempotencyKey(context.HttpContext.Request);
			var userId = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "123";

			if (string.IsNullOrEmpty(userId))
				throw new UnauthorizedAccessException("User ID is missing in the request context.");

			cacheKey = $"Idempotency_{userId}_{idempotencyKey}";

			// request status tracking
			var (isNewlyCreated, cacheEntry) = await GetOrCreateCacheEntryAsync(cacheKey);
			if (cacheEntry != null && !isNewlyCreated)
			{
				context.Result = cacheEntry.Status switch
				{
					"Processing" => new ConflictObjectResult("Request is already being processed. Please wait."),
					"Completed" or "Failed" => new ContentResult
					{
						Content = cacheEntry.Response,
						ContentType = "application/json",
						StatusCode = 200
					},
					_ => throw new InvalidOperationException($"Unknown cache status: {cacheEntry.Status}")
				};

				if (context.Result is ContentResult contentResult)
					context.HttpContext.Response.Headers.Append("X-Idempotent-Response", "true");

				return;
			}

			await MarkRequestAsProcessingAsync(cacheKey);
			var executedContext = await next();

			if (executedContext.Result is ObjectResult objResult && objResult.StatusCode == 200)
			{
				var responseJson = JsonConvert.SerializeObject(objResult.Value);
				await CacheSuccessfulResponseAsync(cacheKey, responseJson);
			}
			else
			{
				await RemoveCacheEntryAsync(cacheKey);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "An error occurred while processing the request.");
			if (cacheKey != null) await RemoveCacheEntryAsync(cacheKey);
			context.Result = new StatusCodeResult(500);
		}
	}

	private async Task<(bool, IdempotencyCacheEntry)> GetOrCreateCacheEntryAsync(string cacheKey)
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		try
		{
			if (_useLock)
			{
				string lockKey = $"{cacheKey}_lock";
				string lockValue = Guid.NewGuid().ToString();
				TimeSpan lockExpiry = TimeSpan.FromSeconds(10);

				bool lockAcquired = await _redisConnectionWrapper.AcquireLockAsync(lockKey, lockValue, lockExpiry);
				if (!lockAcquired) throw new InvalidOperationException("Failed to acquire Redis lock.");

				try
				{
					return await GetOrCreateCacheEntryWithoutLockAsync(cacheKey, cts.Token);
				}
				finally
				{
					await _redisConnectionWrapper.ReleaseLockAsync(lockKey, lockValue);
				}
			}
			else
			{
				return await GetOrCreateCacheEntryWithoutLockAsync(cacheKey, cts.Token);
			}
		}
		catch (OperationCanceledException ex)
		{
			_logger.LogWarning($"Cache operation timed out for key {cacheKey}: {ex.Message}");
			throw new TimeoutException($"Cache operation timed out for key: {cacheKey}", ex);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, $"Cache operation failed for key {cacheKey}: {ex.Message}");
			throw new InvalidOperationException($"Cache operation failed for key: {cacheKey}", ex);
		}
	}

	private async Task<(bool, IdempotencyCacheEntry)> GetOrCreateCacheEntryWithoutLockAsync(string cacheKey, CancellationToken cancellationToken)
	{
		var cacheData = await _distributedCache.GetAsync(cacheKey, cancellationToken);
		if (cacheData != null)
		{
			return (false, JsonConvert.DeserializeObject<IdempotencyCacheEntry>(Encoding.UTF8.GetString(cacheData)));
		}

		var cacheEntry = new IdempotencyCacheEntry { Status = "Processing", Response = null };
		var cacheEntryData = JsonConvert.SerializeObject(cacheEntry);

		await _distributedCache.SetAsync(
			cacheKey,
			Encoding.UTF8.GetBytes(cacheEntryData),
			new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) },
			cancellationToken
		);

		return (true, cacheEntry);
	}

	private async Task MarkRequestAsProcessingAsync(string cacheKey)
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var cacheEntry = new IdempotencyCacheEntry { Status = "Processing" };
		var cacheEntryData = JsonConvert.SerializeObject(cacheEntry);

		await _distributedCache.SetAsync(
			cacheKey,
			Encoding.UTF8.GetBytes(cacheEntryData),
			new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) },
			cts.Token
		);
	}

	private async Task CacheSuccessfulResponseAsync(string cacheKey, string responseJson)
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var cacheEntry = new IdempotencyCacheEntry { Status = "Completed", Response = responseJson };
		var cacheEntryData = JsonConvert.SerializeObject(cacheEntry);

		await _distributedCache.SetAsync(
			cacheKey,
			Encoding.UTF8.GetBytes(cacheEntryData),
			new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) },
			cts.Token
		);
	}

	private async Task RemoveCacheEntryAsync(string cacheKey)
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await _distributedCache.RemoveAsync(cacheKey, cts.Token);
	}

	public Ulid ExtractAndValidateIdempotencyKey(HttpRequest httpRequest)
	{
		const string idempotencyHeaderName = "Idempotency-Key";

		if (!httpRequest.Headers.TryGetValue(idempotencyHeaderName, out var value))
			throw new ArgumentNullException(idempotencyHeaderName, "The Idempotency-Key header is missing.");

		var idempotencyKeyValue = value.ToString();

		if (string.IsNullOrWhiteSpace(idempotencyKeyValue))
			throw new ArgumentException("The Idempotency-Key value cannot be empty.");

		if (!Ulid.TryParse(idempotencyKeyValue, out var idempotencyKey))
			throw new ArgumentException($"Invalid Idempotency-Key format: {idempotencyKeyValue}");

		return idempotencyKey;
	}
}

public class IdempotencyCacheEntry
{
	public string Status { get; set; }
	public string Response { get; set; }
}