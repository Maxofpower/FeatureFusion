using Asp.Versioning.ApiExplorer;
using BuildingBlocks.Mcp;
using BuildingBlocks.Mcp.Hosting;
using BuildingBlocks.Mediator;
using BuildingBlocks.Mediator.DependencyInjection;
using Enyim.Caching;
using Enyim.Caching.Configuration;
using EventBusRabbitMQ;
using FeatureFusion.Features.MediatorDemo.Endpoints;
using FeatureFusion.Features.Products.Endpoints;
using FeatureFusion.Infrastructure.Behaviors;
using FeatureFusion.Infrastructure.Exceptions;
using FeatureFusion.Infrastructure.Extensions;
using FeatureFusion.API.V2;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.FeatureManagement;
using OpenTelemetry.Trace;
using System.Reflection;
using static RedisSettings;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
	.SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
	.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: false, reloadOnChange: true)
	.AddEnvironmentVariables(); 

builder.AddServiceDefaults(
	configureOptions: o =>
	{
		o.IntegrateMediator = true;
		o.IntegrateMcp = true;
		o.Instrumentation.Npgsql = true;
		o.Instrumentation.EventBus = true;
	},
	configureTelemetry: telemetry =>
	{
		telemetry.AddSource("DbMigrations");
		telemetry.ConfigureTracing(t => t
			.AddEntityFrameworkCoreInstrumentation()
			.AddRedisInstrumentation());
	});

// Use the generic method for JWT Authentication
builder.Services.AddJwtAuthentication(builder.Configuration);

builder.Services.AddMemoryCache();

builder.Services.AddFeatureManagementWithFilters<UseGreetingFilter>();

builder.Services.RegisterServices();

builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<PaginationExceptionHandler>();

builder.Services.AddMediator(cfg =>
{
	cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    // Host pipeline behaviors (order: lower = outermost). 
    // Telemetry is NOT a pipeline — optional Send wrap.
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>), order: 0);
	cfg.UseTelemetry();
	cfg.ValidateOnStartup = true;
});

if (builder.Environment.IsDevelopment())
{
	builder.Services.AddBuildingBlocksMcp(o =>
	{
		o.ScanAssembly(Assembly.GetExecutingAssembly());
		o.UseTelemetry();
		o.UseMemoryIdempotency(TimeSpan.FromHours(1));
	}).UseDispatcher(async (sp, msg, ct) =>
	{
		await using var scope = sp.CreateAsyncScope();
		return await scope.ServiceProvider.GetRequiredService<ISender>().Send(msg, ct);
	});
}


builder.Services.AddApiVersioningWithReader();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor(); // Register IHttpContextAccessor

builder.Services.AddSwaggerConfiguration();


builder.Services.AddOptions<EventBusOptions>()
	.Bind(builder.Configuration.GetSection("EventBus"))
	.ValidateDataAnnotations() 
	.ValidateOnStart();

builder.AddApplicationServices(); 

#region Cache Provider

var memcachedSection = builder.Configuration.GetSection("Memcached");

builder.Services
	.AddOptions<MemcachedClientOptions>()
	.Bind(memcachedSection)
	.ValidateDataAnnotations()
	.Validate(options => options.Servers?.Any() ?? false, "At least one Memcached server must be configured")
	.ValidateOnStart();

builder.Services.AddEnyimMemcached();


var redisSection = builder.Configuration.GetSection("Redis");
builder.Services
	.AddOptions<RedisOptions>()
	.Bind(redisSection)
	.ValidateDataAnnotations()
	.Validate(options => !string.IsNullOrEmpty(options.ConnectionString), "Redis connection string is required.")
	.ValidateOnStart();


builder.Services.AddCacheWithRedis(builder.Configuration);

#endregion

var app = builder.Build();

app.MapDefaultEndpoints();
var featureManager = app.Services.GetRequiredService<IFeatureManager>();

//To Present middleware dynamic caching example
var useMemcached = await featureManager.IsEnabledAsync("MemCachedEnabled");
var  useRedis = await featureManager.IsEnabledAsync("IdempotencyEnabled");

// Check if the feature flag is enabled
if (await featureManager.IsEnabledAsync("RecommendationCacheMiddleware"))
{
	// Add the middleware if the feature flag is enabled
	app.UseMiddleware<RecommendationCacheMiddleware>();
}

ConfigureSwaggerUI(app);
ConfigureRequestPipeline(app);

#region Middleware Configuration

void ConfigureSwaggerUI(WebApplication app)
{
	app.UseSwagger();
	app.UseSwaggerUI(options =>
	{
		var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
		foreach (var description in provider.ApiVersionDescriptions.OrderByDescending(d => d.ApiVersion))
		{
			options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName);
		}
	});
}

void ConfigureRequestPipeline(WebApplication app)
{
	// IExceptionHandler (e.g. ValidationException → 400). Do not stack UseDeveloperExceptionPage
	// after this — it turns handled ValidationExceptions into opaque 500s in Development/tests.
	app.UseExceptionHandler();

	// Cursor HTTP MCP uses http://localhost:5141/mcp. HTTPS redirection would 307 to
	// https://localhost:7226/mcp and the MCP client hangs on the Kestrel dev cert.
	app.UseWhen(
		ctx => !ctx.Request.Path.StartsWithSegments("/mcp"),
		branch => branch.UseHttpsRedirection());
	app.UseAuthorization();

	// Map API controllers and versioned routes
	app.MapControllers();
	if (app.Environment.IsDevelopment())
		app.MapBuildingBlocksMcp();
}
#endregion

app.MapGreetingApiV2();
app.MapMediatorDemoEndpoints();
app.MapProductPaginationEndpoints();

#region memchached prestart up validation if enabled
// Pre-startup validation for memcached and redis
if (useMemcached)
{
	using var scope = app.Services.CreateScope();
	var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
	var memcachedClient = scope.ServiceProvider.GetRequiredService<IMemcachedClient>();

	try
	{
		logger.LogInformation("Validating Memcached connection...");

		// Test write/read operation
		var testKey = $"startup_test_{Guid.NewGuid()}";
		var testValue = "connection_test_value";
		var expiration = TimeSpan.FromSeconds(5);

		// Write test value
		var setSuccess = await memcachedClient.SetAsync(testKey, testValue, expiration);
		if (!setSuccess)
		{
			throw new InvalidOperationException("Failed to write test value to Memcached");
		}

		// Read test value
		var retrievedValue = await memcachedClient.GetAsync<string>(testKey);
		if (retrievedValue?.Value != testValue)
		{
			throw new InvalidOperationException("Failed to read test value from Memcached");
		}

		logger.LogInformation("Memcached connection validated successfully");
	}
	catch (Exception ex)
	{
		logger.LogCritical(ex, "Memcached pre-startup validation failed");
		throw; // Prevent application startup
	}
}
if(useRedis)
{
	using var scope = app.Services.CreateScope();
	var redis = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
	try
	{
		await redis.GetAsync("tese");
	}
	catch (Exception ex)
	{
		Console.WriteLine($"[Redis startup Error] {ex.Message}");
		throw;
	}
}

#endregion


app.Run();



