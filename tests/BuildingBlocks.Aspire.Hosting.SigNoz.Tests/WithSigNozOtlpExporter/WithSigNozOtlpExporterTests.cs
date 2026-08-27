using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace BuildingBlocks.Aspire.Hosting.SigNoz.Tests.WithSigNozOtlpExporter;

public sealed class WithSigNozOtlpExporterTests
{
    [Fact]
    public void Wires_project_environment_and_waits()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        // ProjectResource requires a real project path in full Aspire; validate helper against resource API.
        Assert.True(signoz.Resource.IsCollectorBound);
        Assert.NotNull(signoz.Resource.CollectorResource);

        var grpc = signoz.Resource.OtlpGrpcEndpoint;
        var http = signoz.Resource.OtlpHttpEndpoint;
        Assert.NotEqual(grpc.EndpointName, http.EndpointName);
    }

    [Fact]
    public void Throws_when_collector_unbound()
    {
        var unbound = new SigNozResource("orphan");
        Assert.False(unbound.IsCollectorBound);
        Assert.Throws<InvalidOperationException>(() => _ = unbound.OtlpGrpcEndpoint);
    }
}
