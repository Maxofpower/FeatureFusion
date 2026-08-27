using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;

namespace BuildingBlocks.Telemetry.Internal.Pipeline;

/// <summary>
/// Builds and mutates the OpenTelemetry <see cref="ResourceBuilder"/> for the service.
/// </summary>
internal static class TelemetryResourceConfigurator
{
    /// <summary>
    /// Creates a resource builder with service identity and environment attributes.
    /// </summary>
    public static ResourceBuilder CreateResourceBuilder(
        string serviceName,
        TelemetryOptions options,
        IHostEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: serviceName,
                serviceNamespace: options.ServiceNamespace,
                serviceVersion: options.ServiceVersion,
                serviceInstanceId: Environment.MachineName);

        ApplyResourceAttributes(resourceBuilder, options, environment);
        return resourceBuilder;
    }

    /// <summary>
    /// Applies service identity and environment attributes onto an existing resource builder.
    /// </summary>
    public static void ApplyResource(
        ResourceBuilder resourceBuilder,
        string serviceName,
        TelemetryOptions options,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(resourceBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        resourceBuilder.AddService(
            serviceName: serviceName,
            serviceNamespace: options.ServiceNamespace,
            serviceVersion: options.ServiceVersion,
            serviceInstanceId: Environment.MachineName);

        ApplyResourceAttributes(resourceBuilder, options, environment);
    }

    private static void ApplyResourceAttributes(
        ResourceBuilder resourceBuilder,
        TelemetryOptions options,
        IHostEnvironment environment)
    {
        var attributes = new List<KeyValuePair<string, object>>
        {
            new("deployment.environment", environment.EnvironmentName),
            new("service.environment", environment.EnvironmentName),
        };

        foreach (var kv in options.ResourceAttributes)
        {
            attributes.Add(new KeyValuePair<string, object>(kv.Key, kv.Value));
        }

        resourceBuilder.AddAttributes(attributes);
    }
}
