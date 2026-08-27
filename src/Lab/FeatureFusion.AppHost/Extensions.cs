using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Configuration;
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

	/// <summary>
	/// Binds SigNoz UI port and admin credentials from the <c>SigNoz</c> configuration section.
	/// Override with environment variables: <c>SigNoz__UiPort</c>, <c>SigNoz__AdminEmail</c>,
	/// <c>SigNoz__AdminPassword</c>, <c>SigNoz__AdminName</c>, <c>SigNoz__OrgName</c>.
	/// </summary>
	public static IResourceBuilder<SigNozResource> WithUiFromConfiguration(
		this IResourceBuilder<SigNozResource> signoz,
		IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(signoz);
		ArgumentNullException.ThrowIfNull(configuration);

		var section = configuration.GetSection("SigNoz");
		return signoz.WithUi(
			port: section.GetValue<int?>("UiPort"),
			adminEmail: EmptyToNull(section["AdminEmail"]),
			adminPassword: EmptyToNull(section["AdminPassword"]),
			adminName: EmptyToNull(section["AdminName"]),
			orgName: EmptyToNull(section["OrgName"]));
	}

	private static string? EmptyToNull(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value;

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
