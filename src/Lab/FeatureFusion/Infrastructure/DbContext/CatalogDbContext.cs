using FeatureFusion.Infrastructure.EntitiyConfiguration;
using Microsoft.EntityFrameworkCore;
using EventBusRabbitMQ.Extensions;
using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Infrastructure.Context;
using FeatureFusion.Domain.Carts;
using FeatureFusion.Domain.Catalog;
using FeatureFusion.Domain.Customers;
using FeatureFusion.Domain.Orders;
using FeatureFusion.Domain.Payments;
using FeatureFusion.Features.Admission;

namespace FeatureFusion.Infrastructure.Context;

public class CatalogDbContext : DbContext, IEventStoreDbContext
{
	public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
		: base(options)
	{
	}

	public DbSet<OutboxMessage> OutboxMessages { get; set; }
	public DbSet<InboxMessage> InboxMessages { get; set; }
	public DbSet<ProcessedMessage> ProcessedMessages { get; set; }
	public DbSet<InboxSubscriber> InboxSubscriber { get; set; }
	public DbSet<Product> Product { get; set; }
	public DbSet<Brand> Brands { get; set; }
	public DbSet<Category> Categories { get; set; }
	public DbSet<Customer> Customers { get; set; }
	public DbSet<Order> Orders { get; set; }
	public DbSet<OrderItem> OrderItems { get; set; }
	public DbSet<Cart> Carts { get; set; }
	public DbSet<CartItem> CartItems { get; set; }
	public DbSet<PaymentRecord> PaymentRecords { get; set; }
	public DbSet<IntentTicket> IntentTickets { get; set; }

	protected override void OnModelCreating(ModelBuilder builder)
	{
		builder.ApplyConfiguration(new ProductEntityTypeConfiguration());
		builder.ApplyConfiguration(new BrandEntityTypeConfiguration());
		builder.ApplyConfiguration(new CategoryEntityTypeConfiguration());
		builder.ApplyConfiguration(new CustomerEntityTypeConfiguration());
		builder.ApplyConfiguration(new OrderEntityTypeConfiguration());
		builder.ApplyConfiguration(new OrderItemEntityTypeConfiguration());
		builder.ApplyConfiguration(new ProductImageEntityTypeConfiguration());
		builder.ApplyConfiguration(new ProductSpecificationEntityTypeConfiguration());
		builder.ApplyConfiguration(new IntentTicketEntityTypeConfiguration());
		builder.ApplyConfiguration(new CartEntityTypeConfiguration());
		builder.ApplyConfiguration(new CartItemEntityTypeConfiguration());
		builder.ApplyConfiguration(new PaymentRecordEntityTypeConfiguration());
		builder.UseEventStore();
	}
}
