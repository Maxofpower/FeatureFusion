using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace EventBusRabbitMQ.Infrastructure.Context
{
	public class DatabaseSeeder : IHostedService
	{
		private readonly IServiceProvider _serviceProvider;
		private readonly ILogger<DatabaseSeeder> _logger;

		public DatabaseSeeder(IServiceProvider serviceProvider, ILogger<DatabaseSeeder> logger)
		{
			_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async Task SeedAsync()
		{
			try
			{
				using (var scope = _serviceProvider.CreateScope())
				{
					var dbContext = scope.ServiceProvider.GetRequiredService<EventBusDbContext>();

					_logger.LogInformation("Ensuring the database and tables are created...");
					await dbContext.Database.EnsureCreatedAsync();

				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "An error occurred while ensuring the database and tables are created.");
			
			}
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			return SeedAsync();
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}
}