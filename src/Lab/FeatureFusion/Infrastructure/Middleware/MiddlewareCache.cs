using FeatureFusion.Dtos;
using FeatureFusion.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

public class RecommendationCacheMiddleware
{
	private readonly RequestDelegate _next;
	private readonly IStaticCacheManager _cacheService;

	public RecommendationCacheMiddleware(RequestDelegate next, IStaticCacheManager cacheService)
	{
		_next = next;
		_cacheService = cacheService;
	}

	public async Task InvokeAsync(HttpContext context)
	{
		// Cache only GET requests
		if (context.Request.Path.Equals("/api/v2/product-recommendation")
			&& context.Request.Method == HttpMethods.Get) 	
		{
			// Dynamically generate a cache key based on user-specific data
			var cacheKey = GenerateUserSpecificCacheKey(context);

			// Attempt to fetch cached data
			var cachedResponse = await _cacheService.TryGetAsync<List<ProductPromotionDto>>(cacheKey) ?? [];
			if (cachedResponse is not null && cachedResponse.Count > 0)
			{
				context.Response.ContentType = "application/json";
				await context.Response.WriteAsync(JsonSerializer.Serialize(cachedResponse)); // Serialize cachedResponse);
				return;
			}

			// Cache the response if not found
			await CacheRecommendationResponseAsync(context, cacheKey, TimeSpan.FromMinutes(10));
		}
		else
		{
			await _next(context);
			return;
		}	
	}

	private static CacheKey GenerateUserSpecificCacheKey(HttpContext context)
	{
		// Example: Combine user ID, path, and query into a unique cache key
		var userId = context.Request.Headers["X-User-Id"].ToString(); // Get user ID from headers
		var path = context.Request.Path.ToString();
		var query = context.Request.QueryString.ToString();

		return new CacheKey($"{userId}_{path}_{query}");
	}

	private async Task CacheRecommendationResponseAsync(HttpContext context, CacheKey cacheKey, TimeSpan cacheDuration)
	{
		var originalResponseBody = context.Response.Body;
		using (var memoryStream = new MemoryStream())
		{
			context.Response.Body = memoryStream;
			//  next middleware in the request pipeline or the endpoint handler (fresh response)
			await _next(context);

			if (context.Response.StatusCode == StatusCodes.Status200OK)
			{
				memoryStream.Seek(0, SeekOrigin.Begin);
				var responseBody = await new StreamReader(memoryStream).ReadToEndAsync();

				var recommendations = JsonSerializer.Deserialize<List<ProductPromotionDto>>(responseBody);

				// Cache the deserialized object
				await _cacheService.SetAsync(cacheKey, recommendations);

				// Write the response back to the client
				memoryStream.Seek(0, SeekOrigin.Begin);
				await memoryStream.CopyToAsync(originalResponseBody);
			}
			else
			{
				memoryStream.Seek(0, SeekOrigin.Begin);
				await memoryStream.CopyToAsync(originalResponseBody);
			}
		}
	}
}
