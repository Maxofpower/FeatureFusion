namespace FeatureFusion.Infrastructure.Initializers
{
	public interface IAppInitializer
	{
		Task InitializeAsync(CancellationToken cancellationToken = default);
	}
	public sealed class AppInitializer(IServiceScopeFactory scopeFactory)
	: IHostedService
	{
		private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			using var scope = _scopeFactory.CreateScope();

			var initializers = scope.ServiceProvider.GetServices<IAppInitializer>();

			foreach (var initializer in initializers)
			{
				//  Run the initializers
				await initializer.InitializeAsync(cancellationToken);
			}
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

}
