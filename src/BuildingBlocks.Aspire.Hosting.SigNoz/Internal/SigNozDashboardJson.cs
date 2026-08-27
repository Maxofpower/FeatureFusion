using System.Text.Json;

namespace Aspire.Hosting.Internal;

/// <summary>
/// Validates SigNoz Dashboards V2 (schema v6, UI ≥ 0.135) JSON before POST /api/v2/dashboards.
/// </summary>
internal static class SigNozDashboardJson
{
    internal const int GridWidth = 12;

    internal static void Validate(string json, string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("widgets", out _))
        {
            throw new InvalidOperationException(
                $"{resourceName}: use spec.panels (object keyed by id), not widgets. SigNoz v0.136 rejects unknown field \"widgets\".");
        }

        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetString() != "v6")
        {
            throw new InvalidOperationException($"{resourceName}: schemaVersion must be \"v6\".");
        }

        if (!root.TryGetProperty("spec", out var spec) || spec.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{resourceName}: missing spec object.");
        }

        if (!spec.TryGetProperty("display", out var display)
            || !display.TryGetProperty("name", out var name)
            || string.IsNullOrWhiteSpace(name.GetString()))
        {
            throw new InvalidOperationException($"{resourceName}: spec.display.name is required.");
        }

        ValidateVariables(spec, resourceName);

        if (!spec.TryGetProperty("panels", out var panels) || panels.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{resourceName}: spec.panels object is required.");
        }

        var panelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var panel in panels.EnumerateObject())
        {
            panelIds.Add(panel.Name);
        }

        if (panelIds.Count == 0)
        {
            throw new InvalidOperationException($"{resourceName}: spec.panels must contain at least one panel.");
        }

        ValidateLayouts(spec, panelIds, resourceName);
    }

    private static void ValidateVariables(JsonElement spec, string resourceName)
    {
        if (!spec.TryGetProperty("variables", out var variables) || variables.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;
        foreach (var variable in variables.EnumerateArray())
        {
            if (variable.TryGetProperty("spec", out var varSpec))
            {
                var allowAll = varSpec.TryGetProperty("allowAllValue", out var all) && all.GetBoolean();
                var allowMultiple = varSpec.TryGetProperty("allowMultiple", out var multi) && multi.GetBoolean();
                if (allowAll && !allowMultiple)
                {
                    throw new InvalidOperationException(
                        $"{resourceName}: spec.variables[{index}]: allowAllValue cannot be set if allowMultiple is not true.");
                }

                if (varSpec.TryGetProperty("plugin", out var plugin)
                    && plugin.TryGetProperty("spec", out var pluginSpec)
                    && pluginSpec.TryGetProperty("type", out _))
                {
                    throw new InvalidOperationException(
                        $"{resourceName}: spec.variables[{index}]: DynamicVariable uses \"signal\", not \"type\".");
                }
            }

            index++;
        }
    }

    private static void ValidateLayouts(JsonElement spec, HashSet<string> panelIds, string resourceName)
    {
        if (!spec.TryGetProperty("layouts", out var layouts) || layouts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{resourceName}: spec.layouts array is required.");
        }

        var layoutIndex = 0;
        foreach (var layout in layouts.EnumerateArray())
        {
            if (!layout.TryGetProperty("kind", out var kind) || kind.GetString() != "Grid")
            {
                throw new InvalidOperationException(
                    $"{resourceName}: spec.layouts[{layoutIndex}]: kind must be \"Grid\" (not GridLayout).");
            }

            if (!layout.TryGetProperty("spec", out var layoutSpec)
                || !layoutSpec.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"{resourceName}: spec.layouts[{layoutIndex}]: spec.items is required.");
            }

            var itemIndex = 0;
            foreach (var item in items.EnumerateArray())
            {
                var x = item.GetProperty("x").GetInt32();
                var width = item.GetProperty("width").GetInt32();
                if (x < 0 || x >= GridWidth)
                {
                    throw new InvalidOperationException(
                        $"{resourceName}: spec.layouts[{layoutIndex}].spec.items[{itemIndex}]: x ({x}) must be less than grid width {GridWidth}.");
                }

                if (width < 1 || x + width > GridWidth)
                {
                    throw new InvalidOperationException(
                        $"{resourceName}: spec.layouts[{layoutIndex}].spec.items[{itemIndex}]: x ({x}) + width ({width}) must be ≤ {GridWidth}.");
                }

                if (item.TryGetProperty("content", out var content)
                    && content.TryGetProperty("$ref", out var href))
                {
                    var reference = href.GetString() ?? string.Empty;
                    const string prefix = "#/spec/panels/";
                    if (!reference.StartsWith(prefix, StringComparison.Ordinal)
                        || !panelIds.Contains(reference[prefix.Length..]))
                    {
                        throw new InvalidOperationException(
                            $"{resourceName}: spec.layouts[{layoutIndex}].spec.items[{itemIndex}]: $ref '{reference}' does not match a panel id.");
                    }
                }

                itemIndex++;
            }

            layoutIndex++;
        }
    }
}
