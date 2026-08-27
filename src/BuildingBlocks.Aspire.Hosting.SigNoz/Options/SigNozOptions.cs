using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Options for <c>AddSigNoz</c>.
/// Image defaults come from internal pinned tags.
/// </summary>
/// <remarks>
/// Prefer passing <c>port</c> / <c>otlpGrpcPort</c> / <c>otlpHttpPort</c> / <c>jwtSecret</c>
/// as method parameters on <c>AddSigNoz</c> when known at the call site.
/// Use this type for image tags, lifetime, and custom collector config path.
/// </remarks>
public sealed class SigNozOptions
{
    /// <summary>
    /// Gets or sets the ClickHouse image repository (without registry).
    /// </summary>
    /// <value>Default: <c>clickhouse/clickhouse-server</c>.</value>
    public string ClickHouseImage { get; set; } = SigNozContainerImageTags.ClickHouseImage;

    /// <summary>
    /// Gets or sets the ClickHouse image tag.
    /// </summary>
    /// <value>Default: <c>25.12.5</c>.</value>
    public string ClickHouseTag { get; set; } = SigNozContainerImageTags.ClickHouseTag;

    /// <summary>
    /// Gets or sets the ZooKeeper image repository.
    /// </summary>
    /// <value>Default: <c>signoz/zookeeper</c>.</value>
    public string ZooKeeperImage { get; set; } = SigNozContainerImageTags.ZooKeeperImage;

    /// <summary>
    /// Gets or sets the ZooKeeper image tag.
    /// </summary>
    /// <value>Default: <c>3.7.1</c>.</value>
    public string ZooKeeperTag { get; set; } = SigNozContainerImageTags.ZooKeeperTag;

    /// <summary>
    /// Gets or sets the SigNoz OTel collector image repository.
    /// </summary>
    /// <value>Default: <c>signoz/signoz-otel-collector</c>.</value>
    public string CollectorImage { get; set; } = SigNozContainerImageTags.CollectorImage;

    /// <summary>
    /// Gets or sets the collector image tag.
    /// </summary>
    /// <value>Default: <c>v0.144.6</c>.</value>
    public string CollectorTag { get; set; } = SigNozContainerImageTags.CollectorTag;

    /// <summary>
    /// Gets or sets the schema migrator image repository.
    /// Defaults to the OTel collector image (SigNoz ≥ 0.113 telemetrystore migrator).
    /// </summary>
    /// <value>Default: <c>signoz/signoz-otel-collector</c>.</value>
    public string SchemaMigratorImage { get; set; } = SigNozContainerImageTags.SchemaMigratorImage;

    /// <summary>
    /// Gets or sets the schema migrator image tag.
    /// </summary>
    /// <value>Default: same as <see cref="CollectorTag"/>.</value>
    public string SchemaMigratorTag { get; set; } = SigNozContainerImageTags.SchemaMigratorTag;

    /// <summary>
    /// Gets or sets the SigNoz UI / query image repository.
    /// </summary>
    /// <value>Default: <c>signoz/signoz</c>.</value>
    public string SigNozImage { get; set; } = SigNozContainerImageTags.SigNozImage;

    /// <summary>
    /// Gets or sets the SigNoz UI image tag.
    /// </summary>
    /// <value>Default: <c>v0.136.1</c> (Dashboards V2).</value>
    public string SigNozTag { get; set; } = SigNozContainerImageTags.SigNozTag;

    /// <summary>
    /// Gets or sets the container lifetime for persistent local data.
    /// </summary>
    /// <value>Default: <see cref="ContainerLifetime.Persistent"/>.</value>
    public ContainerLifetime Lifetime { get; set; } = ContainerLifetime.Persistent;

    /// <summary>
    /// Gets or sets an optional fixed host port for the SigNoz UI (container 8080).
    /// </summary>
    /// <remarks>
    /// Applied by <c>AddSigNoz</c> when set. Prefer the <c>port</c> argument on
    /// <c>AddSigNoz</c> / <c>WithUi</c> when known at the call site.
    /// </remarks>
    public int? UiPort { get; set; }

    /// <summary>
    /// Gets or sets an optional fixed host port for OTLP gRPC (container 4317).
    /// </summary>
    public int? OtlpGrpcPort { get; set; }

    /// <summary>
    /// Gets or sets an optional fixed host port for OTLP HTTP (container 4318).
    /// </summary>
    public int? OtlpHttpPort { get; set; }

    /// <summary>
    /// Gets or sets an optional path to a custom otel-collector config on the host.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, the embedded default collector config is materialized.
    /// </remarks>
    public string? CollectorConfigPath { get; set; }

    /// <summary>
    /// Gets or sets local-dev SigNoz UI admin credentials (login + dashboard seeding).
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="SigNozBuilderExtensions.WithUi"/> at the call site when credentials are known
    /// after <c>AddSigNoz</c>; values set there override these options.
    /// </remarks>
    public SigNozUiCredentials UiCredentials { get; set; } = new();
}
