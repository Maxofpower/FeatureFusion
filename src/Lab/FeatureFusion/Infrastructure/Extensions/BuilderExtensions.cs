using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Asp.Versioning.Conventions;
using FeatureFusion.Features.Order.IntegrationEvents;
using FeatureFusion.Features.Order.IntegrationEvents.EventHandling;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Infrastructure.Caching;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.Filters;
using FeatureFusion.Infrastructure.ValidationProvider;
using FeatureFusion.Models;
using FeatureFusion.Infrastructure.Initializers;
using FeatureFusion.Models.Validator;
using FeatureFusion.Services.Authentication;
using FeatureFusion.Services.FeatureToggleService;
using FeatureFusion.Services.ProductService;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using static FeatureFusion.Features.Orders.Commands.CreateOrderCommandHandler;

namespace FeatureFusion.Infrastructure.Extensions
{
	public static class ServiceConfigurationExtensions
	{
		// Generic method for configuring JWT Authentication
		public static void AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
		{
			var key = Encoding.ASCII.GetBytes(configuration["Jwt:Key"] ?? string.Empty);
			if (key.Length < 32)
			{
				throw new ArgumentException("The key length must be at least 256 bits (32 bytes) long.");
			}

			services.AddAuthentication(options =>
			{
				options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
			})
			.AddJwtBearer(options =>
			{
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateLifetime = true,
					ValidateIssuerSigningKey = true,
					ValidIssuer = configuration["Jwt:Issuer"],
					ValidAudience = configuration["Jwt:Audience"],
					IssuerSigningKey = new SymmetricSecurityKey(key)
				};
			});
		}

		// Generic method for configuring Feature Management with feature filters
		public static void AddFeatureManagementWithFilters<T>(this IServiceCollection services)
			where T : IFeatureFilterMetadata // Ensure T implements IFeatureFilterMetadata
		{
			services.AddFeatureManagement()
				.AddFeatureFilter<T>();
		}
		// Generic method for Swagger configuration
		public static void AddSwaggerConfiguration(this IServiceCollection services)
		{
			services.AddSwaggerGen(c =>
			{
				c.EnableAnnotations();
				var provider = services.BuildServiceProvider().GetRequiredService<IApiVersionDescriptionProvider>();
				foreach (var description in provider.ApiVersionDescriptions)
				{
					c.SwaggerDoc(description.GroupName, new OpenApiInfo
					{
						Title = "API",
						Version = description.ApiVersion.ToString()
					});
					c.UseAllOfToExtendReferenceSchemas();
					c.SchemaFilter<EnumSchemaFilter>();
				}

				// Add JWT Authentication to Swagger
				c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
				{
					Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
					Name = "Authorization",
					In = ParameterLocation.Header,
					Type = SecuritySchemeType.ApiKey,
					Scheme = "Bearer"
				});

				c.AddSecurityRequirement(new OpenApiSecurityRequirement
				{
					{
						new OpenApiSecurityScheme
						{
							Reference = new OpenApiReference
							{
								Type = ReferenceType.SecurityScheme,
								Id = "Bearer"
							},
							Scheme = "oauth2",
							Name = "Bearer",
							In = ParameterLocation.Header
						},
						new List<string>()
					}
				});
			});
		}
		public static void AddCacheWithRedis(this IServiceCollection services, IConfiguration configuration)
		{
			if (configuration == null)
			{
				throw new ArgumentNullException(nameof(configuration), "Configuration cannot be null.");
			}


			var redisConfiguration = configuration["Redis:ConnectionString"];
			if (string.IsNullOrEmpty(redisConfiguration))
			{
				throw new ArgumentNullException(nameof(redisConfiguration), "Redis connection string is missing in the configuration.");
			}


			var redisInstanceName = configuration["Redis:InstanceName"] ?? "MyApp:";


			services.AddSingleton<IConnectionMultiplexer>(sp =>
			{
				var config = StackExchange.Redis.ConfigurationOptions.Parse(redisConfiguration);
				config.AbortOnConnectFail = false; // Continue even if Redis is unavailable
				config.ConnectTimeout = 5000;
				config.SyncTimeout = 5000;
				return ConnectionMultiplexer.Connect(config);
			});


			services.AddStackExchangeRedisCache(options =>
			{
				options.ConnectionMultiplexerFactory = () =>
					Task.FromResult(services.BuildServiceProvider().GetRequiredService<IConnectionMultiplexer>());
				options.InstanceName = redisInstanceName;
			});
		}



		// Generic method for API versioning
		public static void AddApiVersioningWithReader(this IServiceCollection services)
		{
			services.AddApiVersioning(options =>
			{
				//options.AssumeDefaultVersionWhenUnspecified = true;
				options.DefaultApiVersion = new ApiVersion(1, 0);
				options.ReportApiVersions = true;
				options.ApiVersionReader = ApiVersionReader.Combine(
					new QueryStringApiVersionReader("v"),
					new HeaderApiVersionReader("X-Version"),
					new UrlSegmentApiVersionReader()
				);
			})
			.AddApiExplorer(options =>
			{
				options.GroupNameFormat = "'v'V";
				options.SubstituteApiVersionInUrl = true;
			})
			.AddMvc(
				options =>
				{
					// automatically applies an api version namespace onventions
					options.Conventions.Add(new VersionByNamespaceConvention());
				});

		}

		public static void RegisterServices(this IServiceCollection services)
		{

			services.AddProblemDetails();

			services.AddSingleton<IStaticCacheManager, MemoryCacheManager>();

			services.AddSingleton<IDistributedCacheManager, MemcachedCacheManager>();

			//TODO: for manual DistributeCache implementation
			//services.AddKeyedSingleton<IDistributedCacheManager, RedisCacheManager>("redis");

			services.AddScoped<IAuthService, AuthService>();

			services.AddScoped<IProductService, ProductService>();

			services.AddScoped<IAppInitializer, ProductPromotionInitializer>();

			services.AddScoped<IFeatureToggleService, FeatureToggleService>();

			services.AddScoped<IIntegrationEventService, IntegrationEventService>();

			// FluentValidation: dual-register for IValidatorProvider (non-generic)
			// and host ValidationBehavior (closed IValidator<T>).
			services.AddFluentValidationAutoValidation();
			services.AddFluentValidatorsFromAssemblies(Assembly.GetExecutingAssembly());

			services.AddSingleton<IRedisConnectionWrapper, RedisConnectionWrapper>();

			services.AddSingleton<IValidatorProvider, ValidatorProvider>();

			// AppInitializer is registered in AddApplicationServices after DB migrations.
		}

		public static class HealthCheckExtensions
		{
			public static Task WriteResponse(HttpContext context, HealthReport report)
			{
				context.Response.ContentType = "application/json; charset=utf-8";
				return context.Response.WriteAsync(JsonSerializer.Serialize(new
				{
					status = report.Status.ToString(),
					checks = report.Entries.Select(e => new
					{
						name = e.Key,
						status = e.Value.Status.ToString(),
						description = e.Value.Description
					}),
					duration = report.TotalDuration
				}));
			}
		}

		/// <summary>
		/// Registers concrete FluentValidation validators as non-generic <see cref="IValidator"/>
		/// (for <see cref="IValidatorProvider"/>) and each closed <c>IValidator&lt;T&gt;</c>
		/// (for host <c>ValidationBehavior</c>). Skips abstract and open-generic types.
		/// </summary>
		public static IServiceCollection AddFluentValidatorsFromAssemblies(
			this IServiceCollection services,
			params Assembly[] assemblies)
		{
			if (assemblies is null || assemblies.Length == 0)
				assemblies = new[] { Assembly.GetExecutingAssembly() };

			var validatorTypes = assemblies
				.SelectMany(a => a.GetTypes())
				.Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
				.Where(typeof(IValidator).IsAssignableFrom)
				.ToList();

			foreach (var validatorType in validatorTypes)
			{
				services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IValidator), validatorType));
				services.TryAdd(ServiceDescriptor.Singleton(validatorType, validatorType));

				foreach (var closed in validatorType.GetInterfaces()
					         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
				{
					if (services.Any(d => d.ServiceType == closed && d.ImplementationType == validatorType))
						continue;

					services.Add(ServiceDescriptor.Singleton(closed, validatorType));
				}
			}

			return services;
		}

		/// <summary>Obsolete alias  prefer <see cref="AddFluentValidatorsFromAssemblies"/>.</summary>
		public static void AddAllValidators(this IServiceCollection services, params Assembly[] assemblies)
			=> services.AddFluentValidatorsFromAssemblies(assemblies);

		public static void AddApplicationServices(this IHostApplicationBuilder builder)
		{

			builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb", configureDbContextOptions: dbContextOptionsBuilder =>
			{
				dbContextOptionsBuilder.UseNpgsql(
				npgsqlOptions =>
				{
					npgsqlOptions.EnableRetryOnFailure(
						maxRetryCount: 5,
						maxRetryDelay: TimeSpan.FromSeconds(5),
						errorCodesToAdd: null); ;
				});
			});
			// Migrations must register before OutboxWorker / AppInitializer so
			// MigrationHostedService.StartAsync completes before they begin work.
			builder.Services.AddMigration<CatalogDbContext, CatalogDContextSeed>();

			// Resolve catalogdb from config at runtime (Aspire/WAF injects ConnectionStrings__catalogdb).
			builder.AddRabbitMqEventBus("eventbus")
				.AddSubscription<OrderCreatedIntegrationEvent, OrderCreatedIntegrationEventHandler>()
				.AddEventDbContext<CatalogDbContext>();

			builder.Services.AddHostedService<AppInitializer>();
		}
	}


}

