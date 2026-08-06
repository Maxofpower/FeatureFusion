using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Asp.Versioning.Conventions;
using FeatureFusion.Features.Order.IntegrationEvents;
using FeatureFusion.Features.Order.IntegrationEvents.EventHandling;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Infrastructure.Caching;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Infrastructure.CQRS;
using FeatureFusion.Infrastructure.CQRS.Adapter;
using FeatureFusion.Infrastructure.Filters;
using FeatureFusion.Infrastructure.ValidationProvider;
using FeatureFusion.Models;
using FeatureManagementFilters.Infrastructure.Caching;
using FeatureManagementFilters.Infrastructure.Initializers;
using FeatureManagementFilters.Models.Validator;
using FeatureManagementFilters.Services.Authentication;
using FeatureManagementFilters.Services.FeatureToggleService;
using FeatureManagementFilters.Services.ProductService;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

			var validators = GetValidators();

			//Fluent Validator
			services.AddFluentValidationAutoValidation();

			foreach (var validator in validators)
			{
				services.Add(
					new ServiceDescriptor(
						serviceType: typeof(IValidator),
						implementationType: validator,
						lifetime: ServiceLifetime.Singleton));

				services.Add(
				   new ServiceDescriptor(
					   serviceType: validator,
					   implementationType: validator,
					   lifetime: ServiceLifetime.Singleton));
			}

			// services.AddValidatorsFromAssembly(typeof(Program).Assembly);


			services.AddSingleton<IRedisConnectionWrapper, RedisConnectionWrapper>();

			services.AddSingleton<IValidatorProvider, ValidatorProvider>();

			// AppInitializer is registered in AddApplicationServices after DB migrations.
		}

		public static IServiceCollection AddMediatorServices(this IServiceCollection services, params Assembly[] assemblies)
		{

			services.Scan(scan => scan
				.FromAssemblies(assemblies)
				.AddClasses(classes => classes.AssignableToAny(
					typeof(IRequestHandler<>),
					typeof(IRequestHandler<,>)))
				.AsImplementedInterfaces()
				.WithTransientLifetime());


			services.Scan(scan => scan
				.FromAssemblies(assemblies)
				.AddClasses(classes => classes.AssignableTo(typeof(IPipelineBehavior<,>)))
				.AsImplementedInterfaces()
				.WithTransientLifetime());

			#region void adaptor
			var voidCommandTypes = assemblies
				.SelectMany(a => a.GetTypes())
				.Where(t => typeof(IRequest).IsAssignableFrom(t) &&
						   !typeof(IRequest<>).IsAssignableFrom(t) &&
						   !t.IsAbstract && !t.IsInterface)
				.ToList();

			foreach (var commandType in voidCommandTypes)
			{
				var adapterType = typeof(VoidCommandAdapter<>).MakeGenericType(commandType);
				var serviceType = typeof(IRequestHandler<,>).MakeGenericType(
					typeof(RequestAdapter<>).MakeGenericType(commandType),
					typeof(Unit));

				services.AddTransient(serviceType, provider =>
				{
					var innerHandlerType = typeof(IRequestHandler<>).MakeGenericType(commandType);
					var innerHandler = provider.GetRequiredService(innerHandlerType);
					return ActivatorUtilities.CreateInstance(provider, adapterType, innerHandler);
				});
			}
			#endregion

			services.AddTransient(typeof(IRequestHandler<,>).MakeGenericType(typeof(RequestAdapter<>), typeof(Unit)),
				provider =>
				{
					var requestType = typeof(RequestAdapter<>).GetGenericArguments()[0];
					var innerHandlerType = typeof(IRequestHandler<>).MakeGenericType(requestType);
					var innerHandler = provider.GetRequiredService(innerHandlerType);
					var adapterType = typeof(VoidCommandAdapter<>).MakeGenericType(requestType);
					return ActivatorUtilities.CreateInstance(provider, adapterType, innerHandler);
				});
			services.AddScoped<IMediator, Mediator>();

			return services;
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

		public static void AddAllValidators(this IServiceCollection services, params Assembly[] assemblies)
		{
			if (assemblies == null || assemblies.Length == 0)
			{
				// If no assemblies are provided, scan the current assembly
				assemblies = new[] { Assembly.GetExecutingAssembly() };
			}

			foreach (var assembly in assemblies)
			{
				// Find all types that inherit from BaseValidator<T>
				var validatorTypes = assembly.GetTypes()
					.Where(t => t.BaseType != null &&
								 t.BaseType.IsGenericType &&
								 t.BaseType.GetGenericTypeDefinition() == typeof(BaseValidator<>))
					.ToList();

				foreach (var validatorType in validatorTypes)
				{
					// Get the generic type argument (TModel) from BaseValidator<TModel>
					var modelType = validatorType.BaseType.GetGenericArguments()[0];

					// Register the validator as IValidator<TModel>
					var validatorInterface = typeof(IValidator<>).MakeGenericType(modelType);
					services.AddScoped(validatorInterface, validatorType);
				}
			}
		}
		private static IEnumerable<Type> GetValidators()
		{
			Assembly[] assemblies = new[] { Assembly.GetExecutingAssembly() };

			var validators = assemblies.SelectMany(ass => ass.GetTypes())
					.Where(typeof(IValidator).IsAssignableFrom)
					.Where(t => !t.GetTypeInfo().IsAbstract);

			return validators;
		}

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

