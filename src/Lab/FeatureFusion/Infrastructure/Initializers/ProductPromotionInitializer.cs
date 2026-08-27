using FeatureFusion.Services.ProductService;
using Microsoft.FeatureManagement;

namespace FeatureFusion.Infrastructure.Initializers
{
	public class ProductPromotionInitializer : IAppInitializer
	{
		private readonly IServiceScopeFactory _serviceScopeFactory;
		IFeatureManagerSnapshot _featureManager;
		public ProductPromotionInitializer(IServiceScopeFactory serviceScopeFactory,
			IFeatureManagerSnapshot featureManager)
		{
			_serviceScopeFactory = serviceScopeFactory;
			_featureManager= featureManager;

		}
		public async Task InitializeAsync(CancellationToken cancellationToken = default)
		{
			if (await _featureManager.IsEnabledAsync("BackgroundServiceEnabled"))
			{
				// Execute background service logic
				Console.WriteLine("Background service is running...");
			}
			else
			{
				Console.WriteLine("Background service is disabled...");
				return ;
			}
			using var scope = _serviceScopeFactory.CreateScope();
			var logger = scope.ServiceProvider.GetRequiredService<ILogger<ProductPromotionInitializer>>();
			var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

			logger.LogInformation("==> Starting initialization of Product Promotions...");

			try
			{
				//  Read-through caching
				var promotions = await productService.GetProductPromotionAsync(false,cancellationToken);
				logger.LogInformation(" ==> Product Promotions initialized successfully.");
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to initialize Product Promotions.");
				// Decide to fail or log and continue based on requirements
				throw;
			}

		}

	}
}
