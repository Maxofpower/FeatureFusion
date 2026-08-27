namespace Aspire.Hosting.Internal;

/// <summary>
/// Materializes embedded SigNoz config files (collector YAML, ClickHouse cluster.xml) for bind mounts.
/// </summary>
internal static class SigNozConfigMaterializer
{
    private const string EmbeddedCollectorConfigName =
        "BuildingBlocks.Aspire.Hosting.SigNoz.otel-collector-config.yaml";

    private const string EmbeddedClusterConfigName =
        "BuildingBlocks.Aspire.Hosting.SigNoz.clickhouse-cluster.xml";

    private const string EmbeddedHistogramFunctionName =
        "BuildingBlocks.Aspire.Hosting.SigNoz.clickhouse-histogram-function.xml";

    private const string EmbeddedHistogramUdfServerConfigName =
        "BuildingBlocks.Aspire.Hosting.SigNoz.clickhouse-histogram-udf.xml";

    private const string ClickHousePlaceholder = "tcp://clickhouse:9000";
    private const string ClickHouseHostPlaceholder = "__CLICKHOUSE_HOST__";
    private const string ZooKeeperHostPlaceholder = "__ZOOKEEPER_HOST__";

    public static string MaterializeCollectorConfig(string resourceName, string clickHouseDsn, string? userConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clickHouseDsn);

        var sourcePath = userConfigPath;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            sourcePath = WriteEmbedded(EmbeddedCollectorConfigName, $"signoz-otel-default-{Sanitize(resourceName)}");
        }
        else if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                $"SigNoz collector config was not found at '{sourcePath}'.",
                sourcePath);
        }

        var content = File.ReadAllText(sourcePath)
            .Replace(ClickHousePlaceholder, clickHouseDsn, StringComparison.Ordinal);

        return WriteTemp($"signoz-otel-{Sanitize(resourceName)}", ".yaml", content);
    }

    public static string MaterializeClickHouseClusterConfig(string zooKeeperHost, string clickHouseHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zooKeeperHost);
        ArgumentException.ThrowIfNullOrWhiteSpace(clickHouseHost);

        var content = ReadEmbedded(EmbeddedClusterConfigName)
            .Replace(ZooKeeperHostPlaceholder, zooKeeperHost, StringComparison.Ordinal)
            .Replace(ClickHouseHostPlaceholder, clickHouseHost, StringComparison.Ordinal);

        return WriteTemp($"signoz-ch-cluster-{Sanitize(clickHouseHost)}", ".xml", content);
    }

    public static string MaterializeClickHouseHistogramFunctionConfig()
        => WriteEmbedded(EmbeddedHistogramFunctionName, "signoz-ch-histogram-fn");

    public static string MaterializeClickHouseHistogramUdfServerConfig()
        => WriteEmbedded(EmbeddedHistogramUdfServerConfigName, "signoz-ch-histogram-udf");

    private static string ReadEmbedded(string logicalName)
    {
        var assembly = typeof(SigNozConfigMaterializer).Assembly;
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded resource '{logicalName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string WriteEmbedded(string logicalName, string filePrefix)
    {
        var content = ReadEmbedded(logicalName);
        return WriteTemp($"{filePrefix}-{Environment.ProcessId}-{Guid.NewGuid():N}", Path.GetExtension(logicalName) is { Length: > 0 } ext ? ext : ".tmp", content);
    }

    private static string WriteTemp(string prefix, string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    private static string Sanitize(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        var written = 0;
        foreach (var ch in name)
        {
            buffer[written++] = char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-';
        }

        return new string(buffer[..written]);
    }
}
