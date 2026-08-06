namespace FeatureFusion.AppHost;

/// <summary>
/// Registers backing services for local Aspire orchestration (eShop-style composition).
/// Fixed host ports use <c>isProxied: false</c> so Docker binds them directly and
/// Aspire's proxy does not double-bind the same port.
/// </summary>
internal static class InfrastructureExtensions
{
	public static InfrastructureResources AddInfrastructure(this IDistributedApplicationBuilder builder)
	{
		var memcached = builder.AddContainer("memcached", "memcached", "alpine")
			.WithEndpoint(name: "memcached", port: 11211, targetPort: 11211, isProxied: false);

		var redis = builder.AddRedis("redis")
			.WithDataVolume("redis_data")
			.WithPersistence(
				interval: TimeSpan.FromMinutes(5),
				keysChangedThreshold: 100)
			.WithRedisInsight()
			.WithRedisCommander();

		var rabbitMq = builder.AddRabbitMQ("eventbus")
			.WithEnvironment("RABBITMQ_LOGS", "-")
			.WithVolume("rabbitmq-data", "/var/lib/rabbitmq")
			.WithEndpoint("tcp", endpoint =>
			{
				endpoint.Port = 5672;
				endpoint.TargetPort = 5672;
				endpoint.IsProxied = false;
			})
			.WithEndpoint("management", endpoint =>
			{
				endpoint.Port = 15672;
				endpoint.TargetPort = 15672;
				endpoint.IsProxied = false;
				endpoint.UriScheme = "http";
			})
			.WithLifetime(ContainerLifetime.Persistent);

		var username = builder.AddParameter("username", secret: true, value: "username");
		var password = builder.AddParameter("password", secret: true, value: "password");

		// pgAdmin is dashboard-only tooling; FeatureFusion WaitFor(catalogDb), not pgAdmin.
		// Brief "Unhealthy" in the dashboard while the UI container boots is expected.
		var postgres = builder.AddPostgres("postgres", userName: username, password: password)
			.WithDataVolume("postgres_data")
			.WithPgAdmin(container =>
			{
				container.WithEnvironment("PGADMIN_DEFAULT_EMAIL", "guest@admin.com");
				container.WithEnvironment("PGADMIN_DEFAULT_PASSWORD", "guest");
			})
			.WithLifetime(ContainerLifetime.Persistent)
			.WithEndpoint("tcp", endpoint =>
			{
				endpoint.Port = 5432;
				endpoint.TargetPort = 5432;
				endpoint.IsProxied = false;
			});

		var catalogDb = postgres.AddDatabase("catalogdb");

		return new InfrastructureResources(memcached, redis, rabbitMq, catalogDb);
	}

	public static IResourceBuilder<ProjectResource> WithInfrastructure(
		this IResourceBuilder<ProjectResource> project,
		InfrastructureResources infra)
	{
		return project
			.WaitFor(infra.Memcached)
			.WithEnvironment("Memcached__Servers__0__Address", "localhost")
			.WithEnvironment("Memcached__Servers__0__Port", "11211")
			.WaitFor(infra.Redis)
			// Defer Redis CS so password/TLS flags match final Aspire 13 resource config.
			.WithEnvironment(context =>
			{
				context.EnvironmentVariables["Redis__ConnectionString"] = infra.Redis.Resource.ConnectionStringExpression;
			})
			.WithReference(infra.RabbitMq).WaitFor(infra.RabbitMq)
			.WithReference(infra.CatalogDb).WaitFor(infra.CatalogDb);
	}
}

internal sealed record InfrastructureResources(
	IResourceBuilder<ContainerResource> Memcached,
	IResourceBuilder<RedisResource> Redis,
	IResourceBuilder<RabbitMQServerResource> RabbitMq,
	IResourceBuilder<PostgresDatabaseResource> CatalogDb);
