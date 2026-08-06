using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure;
using EventBusRabbitMQ.Infrastructure.Context;
using EventBusRabbitMQ.Infrastructure.EventBus;
using EventBusRabbitMQ.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using RabbitMQ.Client;
using System;

namespace Microsoft.Extensions.DependencyInjection;

public static class EventBusExtensions
	{

	public static IEventBusBuilder AddRabbitMqEventBus(this IHostApplicationBuilder builder, string connectionName)
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.AddRabbitMQClient(connectionName);

		builder.Services.AddSingleton<IResiliencePipelineProvider, ResiliencePipelineFactory>();
		builder.Services.AddScoped<IMessageDeduplicationService, MessageDeduplicationService>();
		builder.Services.AddSingleton<IRabbitMQPersistentConnection, RabbitMQPersistentConnection>();

		builder.Services.AddScoped<IMessageProcessor, MessageProcessor>();
		builder.Services.AddSingleton<IEventBus, EventBus>();
		builder.Services.AddSingleton<IHostedService>(sp =>
			(EventBus)sp.GetRequiredService<IEventBus>());


		builder.Services.AddSingleton<EventBusSubscriptionInfo>();

		return new EventBusBuilder(builder.Services);
	
	}

	private class EventBusBuilder(IServiceCollection services) : IEventBusBuilder
	{
		public IServiceCollection Services => services;
	}
	public static IEventBusBuilder AddEventDbContext<TDbContext>(
		this IEventBusBuilder builder,
		string? connectionString = null)
		where TDbContext : DbContext, IEventStoreDbContext
	{
		ArgumentNullException.ThrowIfNull(builder);

		builder.Services.AddDbContext<EventBusDbContext>((serviceProvider, options) =>
		{
			var context = serviceProvider.GetRequiredService<TDbContext>();
			options.UseNpgsql(context.Database.GetDbConnection())
				.ConfigureWarnings(warnings => warnings.Ignore(
					RelationalEventId.PendingModelChangesWarning));
		});

		// Resolve the connection string at runtime so Aspire/WAF env overrides win
		// (baking GetConnectionString at registration often captured appsettings localhost).
		builder.Services.AddDbContextFactory<EventBusDbContext>((provider, options) =>
		{
			var cs = ResolveCatalogConnectionString(provider, connectionString);
			options.UseNpgsql(cs)
				.ConfigureWarnings(warnings =>
					warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
		}, lifetime: ServiceLifetime.Scoped);

		builder.Services.AddScoped<ITransactionalOutbox, TransactionalOutbox<TDbContext>>();

		// Poll the same DbContext type that stores outbox rows (not a parallel EventBusDbContext).
		builder.Services.AddHostedService<OutboxWorker<TDbContext>>();

		builder.Services.AddSingleton(provider =>
		{
			var cs = ResolveCatalogConnectionString(provider, connectionString);
			var dataSourceBuilder = new NpgsqlDataSourceBuilder(cs);
			dataSourceBuilder.EnableParameterLogging();
			return dataSourceBuilder.Build();
		});

		return new EventBusBuilder(builder.Services);
	}

	private static string ResolveCatalogConnectionString(IServiceProvider provider, string? connectionString)
	{
		if (!string.IsNullOrWhiteSpace(connectionString))
		{
			return connectionString;
		}

		var config = provider.GetRequiredService<IConfiguration>();
		return config.GetConnectionString("catalogdb")
			?? throw new InvalidOperationException("Connection string 'catalogdb' was not found.");
	}

	// when eventstore dbset are not part of dbcontext
	public static IEventBusBuilder AddSeeder<TDbContext>(
	this IEventBusBuilder builder,
	string connectionName)
	where TDbContext : DbContext, IEventStoreDbContext
	{
		builder.Services.AddHostedService(provider =>
		{
			var logger = provider.GetRequiredService<ILogger<DatabaseSeeder>>();
			return new DatabaseSeeder(provider, logger);
		});

		return new EventBusBuilder(builder.Services);
	}

	public static IEventBusBuilder AddSubscription<TEvent, THandler>(
			this IEventBusBuilder builder)
			where TEvent : IntegrationEvent
			where THandler : class, IIntegrationEventHandler<TEvent>
		{
			builder.Services.AddKeyedTransient<IIntegrationEventHandler, THandler>(typeof(TEvent));
			builder.Services.Configure<EventBusSubscriptionInfo>(o =>
			{
				o.EventTypes[typeof(TEvent).Name] = typeof(TEvent);
			});
			return builder;
		}
	}

public interface IEventBusBuilder
	{
		IServiceCollection Services { get; }
}

