using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure.EventBus;
using FeatureFusion.Infrastructure.Context;

namespace FeatureFusion.Features.Order.IntegrationEvents
{
	public sealed class IntegrationEventService(ILogger<IntegrationEventService> logger,
	IEventBus eventBus,
	CatalogDbContext catalogContext)
	: IIntegrationEventService
	{
		public async Task PublishThroughEventBusAsync(IntegrationEvent evt)
		{
			try
			{
				logger.LogInformation("Publishing integration event: {IntegrationEventId_published} - ({@IntegrationEvent})", evt.Id, evt);

				await ResilientTransaction.New(catalogContext).ExecuteAsync(async () =>
				{
					await catalogContext.SaveChangesAsync();
					await eventBus.PublishAsync(evt, catalogContext.Database.CurrentTransaction);
				});

			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error Publishing integration event: {IntegrationEventId} - ({@IntegrationEvent})", evt.Id, evt);

			}
		}

	}
}