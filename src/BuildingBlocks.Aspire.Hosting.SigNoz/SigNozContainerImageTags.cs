namespace Aspire.Hosting;

/// <summary>
/// Pinned default image coordinates for the SigNoz stack (internal).
/// Bumped with this package — override via <see cref="SigNozOptions"/> or <c>WithImageTag</c>.
/// </summary>
internal static class SigNozContainerImageTags
{
    public const string Registry = "docker.io";

    public const string ClickHouseImage = "clickhouse/clickhouse-server";
    public const string ClickHouseTag = "25.12.5";

    /// <summary>Session init that installs the SigNoz <c>histogramQuantile</c> ClickHouse UDF.</summary>
    public const string UdfInitImage = "alpine";

    public const string UdfInitTag = "3.20.3";

    /// <summary>GitHub release tag under <c>histogram-quantile/&lt;version&gt;</c>.</summary>
    public const string HistogramQuantileVersion = "v0.0.1";

    public const string ZooKeeperImage = "signoz/zookeeper";
    public const string ZooKeeperTag = "3.7.1";

    /// <summary>OTLP collector (also used for telemetrystore migrate after SigNoz 0.113).</summary>
    public const string CollectorImage = "signoz/signoz-otel-collector";

    public const string CollectorTag = "v0.144.6";

    /// <summary>
    /// Schema migrator defaults to the collector image (migrate bootstrap/sync/async).
    /// </summary>
    public const string SchemaMigratorImage = CollectorImage;

    public const string SchemaMigratorTag = CollectorTag;

    public const string SigNozImage = "signoz/signoz";

    /// <summary>UI ≥ 0.135 required for Dashboards V2 / interactivity.</summary>
    public const string SigNozTag = "v0.136.1";
}
