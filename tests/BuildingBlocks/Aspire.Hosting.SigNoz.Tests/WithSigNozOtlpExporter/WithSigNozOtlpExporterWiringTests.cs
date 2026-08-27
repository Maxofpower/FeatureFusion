using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace BuildingBlocks.Aspire.Hosting.SigNoz.Tests.WithSigNozOtlpExporter;

public sealed class WithSigNozOtlpExporterWiringTests
{
    [Fact]
    public void WithSigNozOtlpExporter_sets_otlp_env_on_project()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");
        var project = builder.AddResource(new ProjectResource("api"))
            .WithSigNozOtlpExporter(signoz);

        Assert.True(
            project.Resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var envAnnotations));
        Assert.NotEmpty(envAnnotations);

        var env = GetEnvironmentVariables(project.Resource);
        Assert.True(env.ContainsKey("OTEL_EXPORTER_OTLP_ENDPOINT"));
        Assert.True(env.TryGetValue("OTEL_EXPORTER_OTLP_PROTOCOL", out var protocol));
        Assert.Equal("grpc", protocol);

        Assert.False(
            project.Resource.TryGetAnnotationsOfType<WaitAnnotation>(out var waitAnnotations)
            && waitAnnotations.Any(w => ReferenceEquals(w.Resource, signoz.Resource.CollectorResource)));
    }

    [Fact]
    public void WithSigNozOtlpExporter_http_protocol_sets_http_protobuf()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");
        var project = builder.AddResource(new ProjectResource("api-http"))
            .WithSigNozOtlpExporter(signoz, SigNozOtlpProtocol.HttpProtobuf);

        var env = GetEnvironmentVariables(project.Resource);
        Assert.True(env.TryGetValue("OTEL_EXPORTER_OTLP_PROTOCOL", out var protocol));
        Assert.Equal("http/protobuf", protocol);
    }

    [Fact]
    public void WithSigNozOtlpExporter_throws_when_collector_unbound()
    {
        var builder = DistributedApplication.CreateBuilder();
        var unbound = builder.AddResource(new SigNozResource("orphan"));
        var project = builder.AddResource(new ProjectResource("api-unbound"));

        Assert.Throws<InvalidOperationException>(() => project.WithSigNozOtlpExporter(unbound));
    }

    [Fact]
    public void AddSigNoz_nests_sidecars_and_exposes_collector_health()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddSigNoz("signoz");

        var collector = Assert.Single(builder.Resources, r => r.Name == "signoz-otel-collector");
        Assert.True(collector.TryGetEndpoints(out var endpoints));
        Assert.Contains(endpoints, e => e.Name == "health");

        var clickhouse = Assert.Single(builder.Resources, r => r.Name == "signoz-clickhouse");
        Assert.True(
            clickhouse.TryGetAnnotationsOfType<ResourceRelationshipAnnotation>(out var relationships));
        Assert.Contains(relationships, r => r.Type == "Parent" || r.Type.Contains("Parent", StringComparison.OrdinalIgnoreCase)
            || r.Resource.Name == "signoz");
    }

    private static Dictionary<string, object> GetEnvironmentVariables(IResource resource)
    {
        var env = new Dictionary<string, object>(StringComparer.Ordinal);
        if (!resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var annotations))
        {
            return env;
        }

        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            env);

        foreach (var annotation in annotations)
        {
            annotation.Callback(context).GetAwaiter().GetResult();
        }

        return env;
    }
}
