using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using EventBusRabbitMQ;
using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure;
using EventBusRabbitMQ.Infrastructure.EventBus;
using EventBusRabbitMQ.Infrastructure.Context;
using EventBusRabbitMQ.Infrastructure.Messaging;
using FeatureFusion.Features.Order.IntegrationEvents.EventHandling;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using IntegrationTests.EventBus;
using IntegrationTests.Infrastructure.EventBusLab;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace IntegrationTests.Aspire;

/// <summary>
/// Shared Aspire-hosted dependencies + WebApplicationFactory for FeatureFusion.
/// EventBus and API suites share this fixture — do not add a parallel Testcontainers stack.
/// </summary>
public sealed class AspireFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
	private readonly DistributedApplication _app;
	private readonly IResourceBuilder<RabbitMQServerResource> _rabbitMq;
	private readonly IResourceBuilder<RedisResource> _redis;
	private readonly IResourceBuilder<PostgresDatabaseResource> _catalogDb;
	private readonly IResourceBuilder<ContainerResource> _memcached;
	private readonly EndpointReference _memcachedEndpoint;
	private readonly int _retryCount = 15;
	private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);

	private string _rabbitMqConnectionString = "amqp://guest:guest@localhost:5672";
	private string _redisConnectionString = "localhost:6379";
	private string _catalogDbConnectionString =
		"Host=localhost;Port=5432;Username=username;Password=password;Database=catalogdb";
	private string _memcachedHost = "localhost";
	private string _memcachedPort = "11211";

	public List<OrderCreatedIntegrationEvent> ProcessedEvents { get; } = new();

	/// <summary>Lab-only EventBus stage journal (Exp 19/20). Cleared by experiments as needed.</summary>
	public EventBusLabJournal EventBusJournal { get; } = new();

	/// <summary>Lab-only fault arming for point B (publish-then-crash). Default off.</summary>
	public EventBusLabFaultController EventBusFaults { get; } = new();

	public AspireFixture()
	{
		EnsureDockerContainerRuntime();

		var options = new DistributedApplicationOptions
		{
			AssemblyName = typeof(AspireFixture).Assembly.FullName,
			DisableDashboard = true
		};
		var appBuilder = DistributedApplication.CreateBuilder(options);

		var rabbitUser = appBuilder.AddParameter("rabbit-user", value: "guest");
		var rabbitPass = appBuilder.AddParameter("rabbit-pass", secret: true, value: "guest");

		// Dynamic ports — avoid colliding with a local AppHost or leftover containers.
		_rabbitMq = appBuilder.AddRabbitMQ("eventbus", rabbitUser, rabbitPass);

		var username = appBuilder.AddParameter("username", secret: true, value: "username");
		var password = appBuilder.AddParameter("password", secret: true, value: "password");
		var postgres = appBuilder.AddPostgres("postgres", userName: username, password: password);
		_catalogDb = postgres.AddDatabase("catalogdb");

		_memcached = appBuilder.AddContainer("memcached", "memcached", "alpine")
			.WithEndpoint(targetPort: 11211, name: "memcached");
		_memcachedEndpoint = _memcached.GetEndpoint("memcached");

		_redis = appBuilder.AddRedis("redis");

		_app = appBuilder.Build();
	}

	private static void EnsureDockerContainerRuntime()
	{
		Environment.SetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME", "docker");
		Environment.SetEnvironmentVariable("DOTNET_ASPIRE_CONTAINER_RUNTIME", "docker");

		const string dockerBin = @"C:\Program Files\Docker\Docker\resources\bin";
		if (OperatingSystem.IsWindows() && Directory.Exists(dockerBin))
		{
			var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
			if (!path.Contains(dockerBin, StringComparison.OrdinalIgnoreCase))
			{
				Environment.SetEnvironmentVariable("PATH", dockerBin + Path.PathSeparator + path);
			}
		}

		if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
		{
			Environment.SetEnvironmentVariable("DOCKER_HOST", "npipe:////./pipe/dockerDesktopLinuxEngine");
		}
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.UseEnvironment("Development");
		builder.ConfigureAppConfiguration((_, config) =>
		{
			config.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["ConnectionStrings:eventbus"] = _rabbitMqConnectionString,
				["ConnectionStrings:catalogdb"] = _catalogDbConnectionString,
				["Aspire:Npgsql:EntityFrameworkCore:PostgreSQL:catalogdb"] = _catalogDbConnectionString,
				["Redis:ConnectionString"] = _redisConnectionString,
				["Memcached:Servers:0:Address"] = _memcachedHost,
				["Memcached:Servers:0:Port"] = _memcachedPort
			});
		});
	}

	protected override IHost CreateHost(IHostBuilder builder)
	{
		builder.ConfigureServices(services =>
		{
			services.Configure<EventBusOptions>(options =>
			{
				options.EnableDeduplication = false;
				options.SubscriptionClientName = "feature_fusion";
				options.RetryCount = 3;
			});

			services.AddScoped<OrderCreatedIntegrationEventHandler>();
			services.AddKeyedScoped<IIntegrationEventHandler<OrderCreatedIntegrationEvent>,
				OrderCreatedIntegrationEventHandler>(typeof(OrderCreatedIntegrationEvent));

			services.AddKeyedScoped<IIntegrationEventHandler<TestIntegrationEvent>,
				TestIntegrationEventHandler>(typeof(TestIntegrationEvent));
			services.AddKeyedScoped<IIntegrationEventHandler, TestIntegrationEventHandler>(
				typeof(TestIntegrationEvent));

			services.AddKeyedScoped<IIntegrationEventHandler<FailingIntegrationEvent>,
				FailingIntegrationEventHandler>(typeof(FailingIntegrationEvent));
			services.AddKeyedScoped<IIntegrationEventHandler, FailingIntegrationEventHandler>(
				typeof(FailingIntegrationEvent));

			services.AddKeyedScoped<IIntegrationEventHandler, TransientThrowingIntegrationEventHandler>(
				typeof(TransientThrowingIntegrationEvent));
			services.AddKeyedScoped<IIntegrationEventHandler, BusinessFailureIntegrationEventHandler>(
				typeof(BusinessFailureIntegrationEvent));
			services.AddKeyedScoped<IIntegrationEventHandler, OnceTransientThenSucceedIntegrationEventHandler>(
				typeof(OnceTransientThenSucceedIntegrationEvent));

			services.AddKeyedScoped<IIntegrationEventHandler>(
				typeof(OrderCreatedIntegrationEvent),
				(sp, key) => new TestEventHandlerDecorator(
					sp.GetRequiredService<OrderCreatedIntegrationEventHandler>(),
					ProcessedEvents));

			// Lab EventBus observation seam (no-op when faults disarmed).
			services.AddSingleton(EventBusJournal);
			services.AddSingleton(EventBusFaults);
			services.AddSingleton<IEventBusLabHook>(sp =>
				new EventBusLabHook(EventBusJournal, EventBusFaults));

			services.RemoveAll<IMessageProcessor>();
			services.AddScoped<MessageProcessor>();
			services.AddScoped<IMessageProcessor>(sp =>
				new LabMessageProcessorDecorator(
					sp.GetRequiredService<MessageProcessor>(),
					EventBusJournal));

			services.Configure<EventBusSubscriptionInfo>(o =>
			{
				o.EventTypes[typeof(OrderCreatedIntegrationEvent).Name] = typeof(OrderCreatedIntegrationEvent);
				o.EventTypes[typeof(TestIntegrationEvent).Name] = typeof(TestIntegrationEvent);
				o.EventTypes[typeof(FailingIntegrationEvent).Name] = typeof(FailingIntegrationEvent);
				o.EventTypes[typeof(TransientThrowingIntegrationEvent).Name] =
					typeof(TransientThrowingIntegrationEvent);
				o.EventTypes[typeof(BusinessFailureIntegrationEvent).Name] =
					typeof(BusinessFailureIntegrationEvent);
				o.EventTypes[typeof(OnceTransientThenSucceedIntegrationEvent).Name] =
					typeof(OnceTransientThenSucceedIntegrationEvent);
			});

			services.AddHostedService(provider =>
			{
				var logger = provider.GetRequiredService<ILogger<DatabaseSeeder>>();
				return new DatabaseSeeder(provider, logger);
			});
		});

		return base.CreateHost(builder);
	}

	public async Task InitializeAsync()
	{
		await _app.StartAsync();

		_rabbitMqConnectionString = await ((IResourceWithConnectionString)_rabbitMq.Resource).GetConnectionStringAsync()
			?? throw new InvalidOperationException("RabbitMQ connection string was not available.");
		_redisConnectionString = await ((IResourceWithConnectionString)_redis.Resource).GetConnectionStringAsync()
			?? throw new InvalidOperationException("Redis connection string was not available.");
		_catalogDbConnectionString = await ((IResourceWithConnectionString)_catalogDb.Resource).GetConnectionStringAsync()
			?? throw new InvalidOperationException("catalogdb connection string was not available.");

		_memcachedHost = _memcachedEndpoint.Host;
		_memcachedPort = _memcachedEndpoint.Port.ToString();

		// Program.cs rebuilds Configuration from JSON; env vars still apply via AddEnvironmentVariables().
		Environment.SetEnvironmentVariable("ConnectionStrings__eventbus", _rabbitMqConnectionString);
		Environment.SetEnvironmentVariable("ConnectionStrings__catalogdb", _catalogDbConnectionString);
		Environment.SetEnvironmentVariable("Aspire__Npgsql__EntityFrameworkCore__PostgreSQL__catalogdb", _catalogDbConnectionString);
		Environment.SetEnvironmentVariable("Redis__ConnectionString", _redisConnectionString);
		Environment.SetEnvironmentVariable("Memcached__Servers__0__Address", _memcachedHost);
		Environment.SetEnvironmentVariable("Memcached__Servers__0__Port", _memcachedPort);

		await WaitForRabbitMQ();
	}

	private async Task WaitForRabbitMQ()
	{
		for (var i = 0; i < _retryCount; i++)
		{
			try
			{
				var factory = new ConnectionFactory
				{
					Uri = new Uri(_rabbitMqConnectionString),
					RequestedConnectionTimeout = TimeSpan.FromSeconds(30)
				};
				await using var connection = await factory.CreateConnectionAsync();
				await using var channel = await connection.CreateChannelAsync();
				if (connection.IsOpen)
				{
					return;
				}
			}
			catch when (i < _retryCount - 1)
			{
				await Task.Delay(_retryDelay);
			}
		}

		throw new InvalidOperationException($"Could not connect to RabbitMQ after {_retryCount} attempts");
	}

	public new async Task DisposeAsync()
	{
		Environment.SetEnvironmentVariable("ConnectionStrings__eventbus", null);
		Environment.SetEnvironmentVariable("ConnectionStrings__catalogdb", null);
		Environment.SetEnvironmentVariable("Aspire__Npgsql__EntityFrameworkCore__PostgreSQL__catalogdb", null);
		Environment.SetEnvironmentVariable("Redis__ConnectionString", null);
		Environment.SetEnvironmentVariable("Memcached__Servers__0__Address", null);
		Environment.SetEnvironmentVariable("Memcached__Servers__0__Port", null);

		await base.DisposeAsync();
		await _app.StopAsync();
		if (_app is IAsyncDisposable asyncDisposable)
		{
			await asyncDisposable.DisposeAsync();
		}
	}

	public async Task ResetRabbitMQ()
	{
		using var scope = Services.CreateScope();
		var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

		try
		{
			await eventBus.ResetTopologyAsync();
		}
		catch
		{
			var connection = scope.ServiceProvider.GetRequiredService<IRabbitMQPersistentConnection>();
			await using var channel = await connection.CreateChannelAsync();

			var options = scope.ServiceProvider.GetRequiredService<IOptions<EventBusOptions>>();
			var dlqName = $"{options.Value.SubscriptionClientName}_dlq";

			await channel.QueuePurgeAsync(options.Value.SubscriptionClientName);
			await channel.QueuePurgeAsync(dlqName);
		}
	}
}

public sealed class TestEventHandlerDecorator : IIntegrationEventHandler
{
	private readonly IIntegrationEventHandler _inner;
	private readonly List<OrderCreatedIntegrationEvent> _trackedEvents;

	public TestEventHandlerDecorator(
		IIntegrationEventHandler inner,
		List<OrderCreatedIntegrationEvent> trackedEvents)
	{
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));
		_trackedEvents = trackedEvents ?? throw new ArgumentNullException(nameof(trackedEvents));
	}

	public async Task Handle(IntegrationEvent @event)
	{
		if (@event is OrderCreatedIntegrationEvent orderEvent)
		{
			_trackedEvents.Add(orderEvent);
		}
		await _inner.Handle(@event);
	}
}
