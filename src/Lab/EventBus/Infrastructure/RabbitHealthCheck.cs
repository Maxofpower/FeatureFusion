using EventBusRabbitMQ.Infrastructure.EventBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventBusRabbitMQ.Infrastructure
{
	internal class RabbitMQHealthCheck : IHealthCheck
	{
		private readonly IRabbitMQPersistentConnection _connection;

		public RabbitMQHealthCheck(IRabbitMQPersistentConnection connection)
		{
			_connection = connection;
		}

		public async Task<HealthCheckResult> CheckHealthAsync(
			HealthCheckContext context,
			CancellationToken cancellationToken = default)
		{
			try
			{
				if (!_connection.IsConnected)
				{
					await _connection.TryConnectAsync(cancellationToken);
				}

				return _connection.IsConnected
					? HealthCheckResult.Healthy()
					: HealthCheckResult.Unhealthy();
			}
			catch (Exception ex)
			{
				return HealthCheckResult.Unhealthy(exception: ex);
			}
		}
	}
}
