using System.Text.Json;
using Aspire.Hosting.Internal;
using Xunit;

namespace BuildingBlocks.Aspire.Hosting.SigNoz.Tests.WithDashboards;

public sealed class SigNozDashboardListTests
{
    [Fact]
    public void List_response_v2_shape_enumerates_dashboards()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "status": "success",
              "data": {
                "dashboards": [
                  { "id": "a", "name": "asp-net-core-metrics-b6nt9j7b", "spec": { "display": { "name": "ASP .NET Core Metrics" } } },
                  { "id": "b", "name": "buildingblocks-telemetry-3eon8wuf", "spec": { "display": { "name": "BuildingBlocks Telemetry" } } }
                ],
                "total": 2
              }
            }
            """);

        var titles = SigNozDashboardSeeder.EnumerateListedDashboardsForTest(doc.RootElement.GetProperty("data"))
            .Select(SigNozDashboardSeeder.TryGetTitle)
            .Where(static t => t is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(2, titles.Count);
        Assert.Contains("ASP .NET Core Metrics", titles);
        Assert.Contains("BuildingBlocks Telemetry", titles);
    }

    [Fact]
    public void TryGetTitle_prefers_display_name_over_generated_resource_name()
    {
        using var doc = JsonDocument.Parse(
            """{"name":"buildingblocks-telemetry-3eon8wuf","spec":{"display":{"name":"BuildingBlocks Telemetry"}}}""");

        Assert.Equal("BuildingBlocks Telemetry", SigNozDashboardSeeder.TryGetTitle(doc.RootElement));
    }

    [Fact]
    public void TryGetTitle_uses_spec_display_name_for_create_payload()
    {
        using var doc = JsonDocument.Parse(
            """{"spec":{"display":{"name":"BuildingBlocks Telemetry"}}}""");

        Assert.Equal("BuildingBlocks Telemetry", SigNozDashboardSeeder.TryGetTitle(doc.RootElement));
    }
}
