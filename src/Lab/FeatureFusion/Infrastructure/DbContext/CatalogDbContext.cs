using FeatureFusion.Infrastructure.EntitiyConfiguration;
using Microsoft.EntityFrameworkCore;
using EventBusRabbitMQ.Extensions;
using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Infrastructure.Context;
using System.ComponentModel.DataAnnotations.Schema;
using FeatureFusion.Domain.Entities;

namespace FeatureFusion.Infrastructure.Context;


public class CatalogDbContext : DbContext, IEventStoreDbContext
{
	public CatalogDbContext(DbContextOptions<CatalogDbContext> options, IConfiguration configuration)
		: base(options) 
	{
	}
	public DbSet<OutboxMessage> OutboxMessages { get; set; }
	public DbSet<InboxMessage> InboxMessages { get; set; }
	public DbSet<ProcessedMessage> ProcessedMessages { get; set; }
	public DbSet<InboxSubscriber> InboxSubscriber { get; set; }
	public DbSet<Product> Product { get; set; }

	protected override void OnModelCreating(ModelBuilder builder)
	{
		builder.ApplyConfiguration(new ProductEntityTypeConfiguration());
	    builder.UseEventStore(); 
	}
}
