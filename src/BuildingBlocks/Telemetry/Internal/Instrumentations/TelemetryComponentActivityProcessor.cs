using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.SqlClient;

namespace BuildingBlocks.Telemetry.Internal.Instrumentations;

/// <summary>
/// Adds <see cref="TelemetryComponentTags.TagName"/> to spans whose instrumentations lack Enrich hooks
/// (Npgsql, ActivitySource-only integrations) or use ActivitySource names not covered by Enrich defaults.
/// Also maps common contrib ActivitySource names (EF Core, Redis, gRPC) when a consumer registers those
/// packages via <see cref="TelemetryBuilder.ConfigureTracing"/>.
/// </summary>
internal sealed class TelemetryComponentActivityProcessor : BaseProcessor<Activity>
{
    private static readonly Dictionary<string, string> SourceNameToComponent =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.AspNetCore"] = TelemetryComponentTags.AspNetCore,
            [typeof(AspNetCoreTraceInstrumentationOptions).Assembly.GetName().Name!] =
                TelemetryComponentTags.AspNetCore,
            ["System.Net.Http"] = TelemetryComponentTags.HttpClient,
            ["OpenTelemetry.Instrumentation.EntityFrameworkCore"] =
                TelemetryComponentTags.EntityFrameworkCore,
            ["Npgsql"] = TelemetryComponentTags.Npgsql,
            ["OpenTelemetry.Instrumentation.StackExchangeRedis"] = TelemetryComponentTags.Redis,
            ["StackExchange.Redis"] = TelemetryComponentTags.Redis,
            ["OpenTelemetry.Instrumentation.GrpcNetClient"] = TelemetryComponentTags.GrpcClient,
            ["Grpc.Net.Client"] = TelemetryComponentTags.GrpcClient,
            [typeof(SqlClientTraceInstrumentationOptions).Assembly.GetName().Name!] =
                TelemetryComponentTags.SqlClient,
            [TelemetryDefaults.EventBusActivitySource] = TelemetryComponentTags.EventBus,
            [TelemetryDefaults.MediatorActivitySource] = TelemetryComponentTags.Mediator,
            [TelemetryDefaults.McpActivitySource] = TelemetryComponentTags.Mcp,
            [TelemetryDefaults.MassTransitActivitySource] = TelemetryComponentTags.MassTransit,
        };

    public override void OnStart(Activity data)
    {
        if (data is null || data.GetTagItem(TelemetryComponentTags.TagName) is not null)
        {
            return;
        }

        if (TryResolveComponent(data.Source.Name, out var component))
        {
            data.SetTag(TelemetryComponentTags.TagName, component);
        }
    }

    internal static bool TryResolveComponent(string activitySourceName, out string component)
    {
        if (SourceNameToComponent.TryGetValue(activitySourceName, out component!))
        {
            return true;
        }

        component = string.Empty;
        return false;
    }
}
