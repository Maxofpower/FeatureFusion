using System.Diagnostics;
using BuildingBlocks.Telemetry.Internal.Instrumentations;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Instrumentation.SqlClient;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests.Instrumentations;

public sealed class TelemetryComponentEnrichmentTests
{
    [Fact]
    public void ApplyAspNetCoreDefaults_sets_component_before_user_enricher()
    {
        var options = new AspNetCoreTraceInstrumentationOptions();
        string? userTag = null;

        options.EnrichWithHttpRequest = (_, _) => userTag = "user";

        TelemetryComponentEnrichment.ApplyAspNetCoreDefaults(options);

        using var activity = new Activity("http").Start();
        options.EnrichWithHttpRequest!(activity, null!);

        Assert.Equal(TelemetryComponentTags.AspNetCore, activity.GetTagItem(TelemetryComponentTags.TagName));
        Assert.Equal("user", userTag);
    }

    [Fact]
    public void ApplyHttpClientDefaults_sets_component_before_user_enricher()
    {
        var options = new HttpClientTraceInstrumentationOptions();
        string? userTag = null;

        options.EnrichWithHttpRequestMessage = (_, _) => userTag = "user";

        TelemetryComponentEnrichment.ApplyHttpClientDefaults(options);

        using var activity = new Activity("http-out").Start();
        options.EnrichWithHttpRequestMessage!(activity, null!);

        Assert.Equal(TelemetryComponentTags.HttpClient, activity.GetTagItem(TelemetryComponentTags.TagName));
        Assert.Equal("user", userTag);
    }

    [Fact]
    public void ApplySqlClientDefaults_sets_component_before_user_enricher()
    {
        var options = new SqlClientTraceInstrumentationOptions();
        string? userTag = null;

        options.EnrichWithSqlCommand = (_, _) => userTag = "user";

        TelemetryComponentEnrichment.ApplySqlClientDefaults(options);

        using var activity = new Activity("sql").Start();
        options.EnrichWithSqlCommand!(activity, null!);

        Assert.Equal(TelemetryComponentTags.SqlClient, activity.GetTagItem(TelemetryComponentTags.TagName));
        Assert.Equal("user", userTag);
    }

    [Theory]
    [InlineData("Npgsql", TelemetryComponentTags.Npgsql)]
    [InlineData("EventBus", TelemetryComponentTags.EventBus)]
    [InlineData("BuildingBlocks.Mediator", TelemetryComponentTags.Mediator)]
    [InlineData("MassTransit", TelemetryComponentTags.MassTransit)]
    [InlineData("Microsoft.AspNetCore", TelemetryComponentTags.AspNetCore)]
    [InlineData("System.Net.Http", TelemetryComponentTags.HttpClient)]
    [InlineData("OpenTelemetry.Instrumentation.EntityFrameworkCore", TelemetryComponentTags.EntityFrameworkCore)]
    [InlineData("OpenTelemetry.Instrumentation.StackExchangeRedis", TelemetryComponentTags.Redis)]
    [InlineData("OpenTelemetry.Instrumentation.GrpcNetClient", TelemetryComponentTags.GrpcClient)]
    public void Processor_maps_activity_source_to_component(string sourceName, string expectedComponent)
    {
        Assert.True(
            TelemetryComponentActivityProcessor.TryResolveComponent(sourceName, out var component));
        Assert.Equal(expectedComponent, component);
    }

    [Fact]
    public void Processor_skips_when_component_tag_already_set()
    {
        var processor = new TelemetryComponentActivityProcessor();
        using var activity = new Activity("test").Start();
        activity.SetTag(TelemetryComponentTags.TagName, "custom");

        processor.OnStart(activity);

        Assert.Equal("custom", activity.GetTagItem(TelemetryComponentTags.TagName));
    }

    [Fact]
    public void Processor_sets_component_for_npgsql_source()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var processor = new TelemetryComponentActivityProcessor();
        using var source = new ActivitySource("Npgsql");
        using var activity = source.StartActivity("query");
        Assert.NotNull(activity);

        processor.OnStart(activity);

        Assert.Equal(TelemetryComponentTags.Npgsql, activity.GetTagItem(TelemetryComponentTags.TagName));
    }
}
