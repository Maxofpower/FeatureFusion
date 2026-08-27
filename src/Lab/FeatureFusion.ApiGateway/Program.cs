using Enyim.Caching.Configuration;
using FeatureFusion.ApiGateway.RateLimiter.Enums;
using FeatureFusion.ApiGateway.RateLimiter.Enums.Extensions;
using Microsoft.AspNetCore.Builder;
using Yarp.ReverseProxy; // Add this using directive

var builder = WebApplication.CreateBuilder(args);

#region Cache Provider

var memcachedSection = builder.Configuration.GetSection("Memcached");

builder.Services
	.AddOptions<MemcachedClientOptions>()
	.Bind(memcachedSection)
	.ValidateDataAnnotations()
	.Validate(options => options.Servers?.Any() ?? false, "At least one Memcached server must be configured")
	.ValidateOnStart();

builder.Services.AddEnyimMemcached();
builder.Services.AddSingleton<IMemcachedClientFactory, MemcachedClientFactory>();
#endregion

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IMemcachedClientFactory, MemcachedClientFactory>();

// Configure the rate limiter.
builder.Services.AddRateLimiter(options =>
{
	// Switch to standard 429 response
	options.RejectionStatusCode = 429;

	options.AddPolicy<string, MemcachedRateLimiterPolicy>(
		RateLimiterPolicy.MemcachedFixedWindow.GetDisplayName());
});

builder.Services.AddReverseProxy()
			.LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseHttpsRedirection();

app.UseRateLimiter();

app.MapReverseProxy();


app.Run();

