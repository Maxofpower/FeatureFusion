using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFusion.AppHost;

internal static class Extensions
{
	/// <summary>
	/// Sets ASPNETCORE_FORWARDEDHEADERS_ENABLED for all projects (eShop AppHost pattern).
	/// </summary>
	public static IDistributedApplicationBuilder AddForwardedHeaders(this IDistributedApplicationBuilder builder)
	{
		builder.Services.TryAddEventingSubscriber<AddForwardHeadersSubscriber>();
		return builder;
	}

	private sealed class AddForwardHeadersSubscriber : IDistributedApplicationEventingSubscriber
	{
		public Task SubscribeAsync(
			IDistributedApplicationEventing eventing,
			DistributedApplicationExecutionContext executionContext,
			CancellationToken cancellationToken)
		{
			eventing.Subscribe<BeforeStartEvent>((@event, _) =>
			{
				foreach (var project in @event.Model.GetProjectResources())
				{
					project.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
					{
						context.EnvironmentVariables["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
					}));
				}

				return Task.CompletedTask;
			});

			return Task.CompletedTask;
		}
	}
}
