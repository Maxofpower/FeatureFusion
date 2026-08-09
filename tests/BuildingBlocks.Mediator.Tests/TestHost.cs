using BuildingBlocks.Mediator.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Mediator.Tests;

internal static class TestHost
{
	/// <summary>
	/// Registers mediator core without scanning this test assembly (avoids picking up fixture handlers).
	/// </summary>
	public static ServiceProvider Build(Action<IServiceCollection> configure)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddScoped<Mediator>();
		services.AddScoped<ISender>(sp => sp.GetRequiredService<Mediator>());
		services.AddScoped<IMediator>(sp => sp.GetRequiredService<Mediator>());
		configure(services);
		return services.BuildServiceProvider();
	}

	public static ServiceProvider BuildWithAddMediator(Action<MediatorConfiguration> configure, Action<IServiceCollection>? extra = null)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddMediator(configure);
		extra?.Invoke(services);
		return services.BuildServiceProvider();
	}
}

/// <summary>Empty scan target for AddMediator registration tests that must not auto-register fixture handlers.</summary>
public static class AssemblyMarker
{
}
