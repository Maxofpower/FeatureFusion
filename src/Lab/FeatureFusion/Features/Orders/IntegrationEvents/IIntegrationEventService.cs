using EventBusRabbitMQ.Events;

namespace FeatureFusion.Features.Order.IntegrationEvents;

public interface IIntegrationEventService
{
	Task PublishThroughEventBusAsync(IntegrationEvent evt);
}
