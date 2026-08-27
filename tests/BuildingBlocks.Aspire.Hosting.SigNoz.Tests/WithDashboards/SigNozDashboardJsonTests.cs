using System.Text.Json;
using Aspire.Hosting.Internal;
using Xunit;

namespace BuildingBlocks.Aspire.Hosting.SigNoz.Tests.WithDashboards;

public sealed class SigNozDashboardJsonTests
{
    private const string TelemetryDashboardResource =
        "BuildingBlocks.Aspire.Hosting.SigNoz.buildingblocks-telemetry-dashboard.json";

    [Theory]
    [InlineData(TelemetryDashboardResource, "BuildingBlocks Telemetry")]
    [InlineData("BuildingBlocks.Aspire.Hosting.SigNoz.aspnetcore-otlp-v1.json", "ASP .NET Core Metrics")]
    public void Embedded_dashboards_match_signoz_v6_schema(string resourceName, string expectedTitle)
    {
        var json = ReadEmbedded(resourceName);
        Assert.False(string.IsNullOrWhiteSpace(json));

        SigNozDashboardJson.Validate(json, resourceName);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expectedTitle, SigNozDashboardSeeder.TryGetTitle(doc.RootElement));
    }

    [Fact]
    public void Telemetry_dashboard_declares_service_environment_and_component_variables()
    {
        using var doc = JsonDocument.Parse(ReadEmbedded(TelemetryDashboardResource));

        var variables = doc.RootElement
            .GetProperty("spec")
            .GetProperty("variables")
            .EnumerateArray()
            .Select(v => v.GetProperty("spec").GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("service.name", variables);
        Assert.Contains("deployment.environment", variables);
        Assert.Contains("telemetry.component", variables);

        var component = doc.RootElement
            .GetProperty("spec")
            .GetProperty("variables")
            .EnumerateArray()
            .Single(v => v.GetProperty("spec").GetProperty("name").GetString() == "telemetry.component")
            .GetProperty("spec");

        Assert.True(component.GetProperty("allowMultiple").GetBoolean());
        Assert.True(component.GetProperty("allowAllValue").GetBoolean());
        Assert.Equal("traces", component.GetProperty("plugin").GetProperty("spec").GetProperty("signal").GetString());
    }

    [Fact]
    public void Telemetry_dashboard_has_red_component_runtime_and_log_sections()
    {
        using var doc = JsonDocument.Parse(ReadEmbedded(TelemetryDashboardResource));

        var sections = doc.RootElement
            .GetProperty("spec")
            .GetProperty("layouts")
            .EnumerateArray()
            .Select(l => l.GetProperty("spec").GetProperty("display").GetProperty("title").GetString())
            .ToArray();

        Assert.Equal(new[] { "Service RED", "Components", "Runtime", "Logs" }, sections);
    }

    [Fact]
    public void Telemetry_dashboard_covers_metrics_traces_and_logs()
    {
        var queries = ReadBuilderQueries(TelemetryDashboardResource);

        var signals = queries
            .Select(q => q.GetProperty("signal").GetString())
            .Distinct()
            .ToArray();

        Assert.Contains("metrics", signals);
        Assert.Contains("traces", signals);
        Assert.Contains("logs", signals);
    }

    [Fact]
    public void Telemetry_dashboard_trace_queries_use_the_component_attribute()
    {
        var traceQueries = ReadBuilderQueries(TelemetryDashboardResource)
            .Where(q => q.GetProperty("signal").GetString() == "traces")
            .ToArray();

        Assert.NotEmpty(traceQueries);
        Assert.All(traceQueries, query =>
            Assert.Contains(
                "telemetry.component",
                query.GetProperty("filter").GetProperty("expression").GetString()!,
                StringComparison.Ordinal));

        Assert.Contains(
            traceQueries,
            query => query.GetProperty("groupBy").EnumerateArray()
                .Any(g => g.GetProperty("name").GetString() == "telemetry.component"));
    }

    [Fact]
    public void Telemetry_dashboard_runtime_panels_query_net8_and_net9_metric_names()
    {
        var metricNames = ReadBuilderQueries(TelemetryDashboardResource)
            .Where(q => q.GetProperty("signal").GetString() == "metrics")
            .SelectMany(q => q.GetProperty("aggregations").EnumerateArray())
            .Select(a => a.GetProperty("metricName").GetString())
            .ToArray();

        Assert.Contains("dotnet.gc.collections", metricNames);
        Assert.Contains("process.runtime.dotnet.gc.collections.count", metricNames);
        Assert.Contains("dotnet.thread_pool.queue.length", metricNames);
        Assert.Contains("process.runtime.dotnet.thread_pool.queue.length", metricNames);
    }

    [Fact]
    public void Telemetry_dashboard_builder_queries_are_valid_for_query_builder_v5()
    {
        var queries = ReadBuilderQueries(TelemetryDashboardResource);

        Assert.NotEmpty(queries);
        Assert.All(queries, query =>
        {
            Assert.False(string.IsNullOrWhiteSpace(query.GetProperty("name").GetString()));
            Assert.True(query.GetProperty("limit").GetInt32() > 0);
            Assert.True(query.GetProperty("stepInterval").GetInt32() > 0);
            Assert.NotEmpty(query.GetProperty("aggregations").EnumerateArray());
        });

        // Traces and logs aggregate over unbounded cardinality, so they must order deterministically.
        Assert.All(
            queries.Where(q => q.GetProperty("signal").GetString() is "traces" or "logs"),
            query =>
            {
                var order = Assert.Single(query.GetProperty("order").EnumerateArray());
                var orderKey = order.GetProperty("key").GetProperty("name").GetString();
                var aliases = query.GetProperty("aggregations").EnumerateArray()
                    .Select(a => a.GetProperty("alias").GetString())
                    .ToArray();

                Assert.Contains(orderKey, aliases);
                Assert.Equal("desc", order.GetProperty("direction").GetString());
            });
    }

    [Fact]
    public void Telemetry_dashboard_grouped_queries_declare_a_legend_template()
    {
        var grouped = ReadBuilderQueries(TelemetryDashboardResource)
            .Where(q => q.GetProperty("groupBy").GetArrayLength() > 0)
            .ToArray();

        Assert.NotEmpty(grouped);
        Assert.All(grouped, query =>
        {
            var legend = query.GetProperty("legend").GetString();
            Assert.False(string.IsNullOrWhiteSpace(legend));
            Assert.StartsWith("{{", legend, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NeedsReplace_when_embedded_layout_sections_are_missing()
    {
        using var existing = JsonDocument.Parse(
            """
            {"data":{"spec":{"layouts":[{"kind":"Grid","spec":{"display":{"title":"Overview"}}}]}}}
            """);
        using var embedded = JsonDocument.Parse(
            """
            {"spec":{"layouts":[
              {"kind":"Grid","spec":{"display":{"title":"Service RED"}}},
              {"kind":"Grid","spec":{"display":{"title":"Components"}}},
              {"kind":"Grid","spec":{"display":{"title":"Runtime"}}},
              {"kind":"Grid","spec":{"display":{"title":"Logs"}}}
            ]}}
            """);

        Assert.True(SigNozDashboardSeeder.NeedsReplace(existing.RootElement, embedded.RootElement));
    }

    [Fact]
    public void NeedsReplace_is_false_when_existing_has_all_embedded_sections()
    {
        using var existing = JsonDocument.Parse(
            """
            {"spec":{"layouts":[
              {"kind":"Grid","spec":{"display":{"title":"Service RED"}}},
              {"kind":"Grid","spec":{"display":{"title":"Components"}}},
              {"kind":"Grid","spec":{"display":{"title":"Runtime"}}},
              {"kind":"Grid","spec":{"display":{"title":"Logs"}}}
            ]}}
            """);
        using var embedded = JsonDocument.Parse(
            """
            {"spec":{"layouts":[
              {"kind":"Grid","spec":{"display":{"title":"Service RED"}}},
              {"kind":"Grid","spec":{"display":{"title":"Components"}}}
            ]}}
            """);

        Assert.False(SigNozDashboardSeeder.NeedsReplace(existing.RootElement, embedded.RootElement));
    }

    [Fact]
    public void TryGetDashboardId_reads_nested_data()
    {
        using var doc = JsonDocument.Parse("""{"data":{"id":"dash-1","name":"BuildingBlocks Telemetry"}}""");
        Assert.Equal("dash-1", SigNozDashboardSeeder.TryGetDashboardId(doc.RootElement));
    }

    [Fact]
    public void TryGetTitle_reads_nested_list_payload()
    {
        using var doc = JsonDocument.Parse(
            """{"id":"1","data":{"spec":{"display":{"name":"BuildingBlocks Telemetry"}}}}""");

        Assert.Equal("BuildingBlocks Telemetry", SigNozDashboardSeeder.TryGetTitle(doc.RootElement));
    }

    [Fact]
    public void Rejects_widgets_and_gridlayout()
    {
        const string json = """
            {
              "schemaVersion": "v6",
              "widgets": [],
              "spec": {
                "display": { "name": "Bad" },
                "panels": { "p1": { "kind": "Panel" } },
                "layouts": [ { "kind": "GridLayout", "spec": { "items": [] } } ]
              }
            }
            """;

        Assert.Throws<InvalidOperationException>(() => SigNozDashboardJson.Validate(json, "bad.json"));
    }

    [Fact]
    public void Rejects_layout_item_past_grid_width()
    {
        const string json = """
            {
              "schemaVersion": "v6",
              "spec": {
                "display": { "name": "Bad" },
                "panels": { "p1": { "kind": "Panel" } },
                "layouts": [{
                  "kind": "Grid",
                  "spec": {
                    "items": [{ "x": 12, "y": 0, "width": 6, "height": 8, "content": { "$ref": "#/spec/panels/p1" } }]
                  }
                }]
              }
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => SigNozDashboardJson.Validate(json, "bad.json"));
        Assert.Contains("grid width", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Flattens every builder query in a dashboard, both standalone and inside composite queries.
    /// </summary>
    private static List<JsonElement> ReadBuilderQueries(string resourceName)
    {
        var document = JsonDocument.Parse(ReadEmbedded(resourceName));
        var queries = new List<JsonElement>();

        foreach (var panel in document.RootElement.GetProperty("spec").GetProperty("panels").EnumerateObject())
        {
            foreach (var query in panel.Value.GetProperty("spec").GetProperty("queries").EnumerateArray())
            {
                var plugin = query.GetProperty("spec").GetProperty("plugin");
                var pluginSpec = plugin.GetProperty("spec");

                if (plugin.GetProperty("kind").GetString() == "signoz/CompositeQuery")
                {
                    queries.AddRange(
                        pluginSpec.GetProperty("queries").EnumerateArray().Select(q => q.GetProperty("spec")));
                    continue;
                }

                queries.Add(pluginSpec);
            }
        }

        return queries;
    }

    private static string ReadEmbedded(string logicalName)
    {
        var assembly = typeof(SigNozDashboardSeeder).Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
