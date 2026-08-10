using BuildingBlocks.Mediator;
using FeatureManagementFilters.Models;
using static FeatureFusion.Controllers.V2.OrderController;
using static FeatureFusion.Features.Orders.Commands.CreateOrderCommandHandler;

using FeatureFusion.Features.Order.IntegrationEvents;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Infrastructure.Context;
using FeatureFusion.Domain.Entities;

namespace FeatureFusion.Features.Orders.Commands
{
	public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, Result<OrderResponse>>
	{
		private readonly IServiceProvider _serviceProvider;
		private readonly CatalogDbContext _catalogDbContext;

		public CreateOrderCommandHandler(IServiceProvider serviceProvider,
			CatalogDbContext catalogdbContext)
		{
			_serviceProvider = serviceProvider;
			_catalogDbContext = catalogdbContext;
		}

		public async Task<Result<OrderResponse>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
		{

			// Static in-memory product
			var product = new Product
			{
				Name = "Smartphone",
				Published = true,
				Deleted = false,
				VisibleIndividually = true,
				Price = 599.99m
			};

			// Static in-memory customer
			var customer = new Person
			{
				Name = "John Doe",
				Age = 11111,
			};

			var orderId = Guid.NewGuid();

			var orderTotal = product.Price * request.Quantity;

			var response = new OrderResponse
			{
				OrderId = orderId,
				CustomerName = customer.Name,
				ProductName = product.Name,
				Quantity = request.Quantity,
				TotalAmount = orderTotal,
				OrderDate = DateTime.UtcNow,
				Message = "Order created successfully."
			};
			var evt = new OrderCreatedIntegrationEvent(orderId, orderTotal);

			using var scope = _serviceProvider.CreateScope();
			var integrationService = scope.ServiceProvider.GetRequiredService<IIntegrationEventService>();

			// currently it will be added to catalog , i need to setup table order
			_catalogDbContext.Product.Add(product);
			await integrationService.PublishThroughEventBusAsync(evt);

			return Result<OrderResponse>.Success(response);

		}

		public class OrderResponse
		{
			public Guid OrderId { get; set; }
			public string CustomerName { get; set; }
			public string ProductName { get; set; }
			public int Quantity { get; set; }
			public decimal TotalAmount { get; set; }
			public DateTime OrderDate { get; set; }
			public string Message { get; set; }
		}
	}
}
