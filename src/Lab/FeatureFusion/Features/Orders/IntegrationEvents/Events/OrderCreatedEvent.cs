using EventBusRabbitMQ.Events;

namespace FeatureFusion.Features.Order.IntegrationEvents.Events
{
	public record OrderCreatedIntegrationEvent : IntegrationEvent // if we need fallback to direct publish : IAllowDirectFallback
	{
		public Guid OrderId { get; }
		public decimal Total { get; }

		public OrderCreatedIntegrationEvent(Guid orderId, decimal total)
		{
			OrderId = orderId;
			Total = total;
		}
	}
}
