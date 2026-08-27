using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource that represents a SigNoz local-dev stack (query UI + OTLP via collector sidecars).
/// </summary>
/// <remarks>
/// <c>AddSigNoz</c> always configures the HTTP UI endpoint for the Aspire dashboard.
/// Connection string / <c>WithReference</c> expose the UI URL. Use <see cref="SigNozBuilderExtensions.WithSigNozOtlpExporter"/> to wire OTLP.
/// </remarks>
public sealed class SigNozResource : ContainerResource, IResourceWithConnectionString
{
    /// <summary>
    /// Well-known HTTP UI endpoint name.
    /// </summary>
    public const string PrimaryEndpointName = "http";

    /// <summary>
    /// Alias for <see cref="PrimaryEndpointName"/>.
    /// </summary>
    public const string UiEndpointName = PrimaryEndpointName;

    /// <summary>
    /// Well-known OTLP gRPC endpoint name on the collector.
    /// </summary>
    public const string OtlpGrpcEndpointName = "otlp-grpc";

    /// <summary>
    /// Well-known OTLP HTTP endpoint name on the collector.
    /// </summary>
    public const string OtlpHttpEndpointName = "otlp-http";

    internal const string ClickHouseDataDirectory = "/var/lib/clickhouse";

    internal const string ClickHouseUserScriptsDirectory = "/var/lib/clickhouse/user_scripts";

    internal const string ClickHouseHistogramFunctionPath = "/etc/clickhouse-server/histogram_function.xml";

    internal const string ClickHouseHistogramUdfConfigPath = "/etc/clickhouse-server/config.d/histogram-udf.xml";

    internal const string ZooKeeperDataDirectory = "/bitnami/zookeeper";

    private EndpointReference? _primaryEndpoint;
    private EndpointReference? _otlpGrpcEndpoint;
    private EndpointReference? _otlpHttpEndpoint;
    private IResourceBuilder<ContainerResource>? _collector;
    private IResourceBuilder<ContainerResource>? _clickHouse;
    private IResourceBuilder<ContainerResource>? _zooKeeper;
    private IResourceBuilder<ContainerResource>? _schemaMigrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="SigNozResource"/> class.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    public SigNozResource(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Gets the primary (UI) HTTP endpoint for this resource.
    /// </summary>
    /// <remarks>
    /// Available after <c>AddSigNoz</c> (UI is always configured).
    /// </remarks>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, PrimaryEndpointName);

    /// <summary>
    /// Gets the SigNoz UI HTTP endpoint.
    /// </summary>
    public EndpointReference UiEndpoint => PrimaryEndpoint;

    /// <summary>
    /// Gets the host endpoint reference for the UI.
    /// </summary>
    public EndpointReferenceExpression Host => PrimaryEndpoint.Property(EndpointProperty.Host);

    /// <summary>
    /// Gets the port endpoint reference for the UI.
    /// </summary>
    public EndpointReferenceExpression Port => PrimaryEndpoint.Property(EndpointProperty.Port);

    /// <summary>
    /// Gets the OTLP gRPC endpoint on the SigNoz collector (bound by <c>AddSigNoz</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when collector endpoints are not bound.</exception>
    public EndpointReference OtlpGrpcEndpoint =>
        _otlpGrpcEndpoint
        ?? throw new InvalidOperationException(
            $"{nameof(OtlpGrpcEndpoint)} is not bound. Call AddSigNoz before reading collector endpoints.");

    /// <summary>
    /// Gets the OTLP HTTP endpoint on the SigNoz collector (bound by <c>AddSigNoz</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when collector endpoints are not bound.</exception>
    public EndpointReference OtlpHttpEndpoint =>
        _otlpHttpEndpoint
        ?? throw new InvalidOperationException(
            $"{nameof(OtlpHttpEndpoint)} is not bound. Call AddSigNoz before reading collector endpoints.");

    /// <summary>
    /// Gets the collector resource used for WaitFor / OTLP wiring.
    /// </summary>
    internal IResource CollectorResource => Collector.Resource;

    /// <summary>
    /// Gets the connection string expression for the SigNoz UI URL.
    /// </summary>
    /// <remarks>
    /// OTLP is not the connection string — use <c>WithSigNozOtlpExporter</c>.
    /// </remarks>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{PrimaryEndpoint.Property(EndpointProperty.Url)}");

    /// <summary>
    /// Gets the UI URI expression (same as <see cref="ConnectionStringExpression"/>).
    /// </summary>
    /// <remarks>
    /// Format: <c>http://{host}:{port}</c> (Aspire endpoint URL).
    /// </remarks>
    public ReferenceExpression UriExpression => ConnectionStringExpression;

    /// <summary>
    /// Gets whether collector endpoints have been bound.
    /// </summary>
    public bool IsCollectorBound =>
        _otlpGrpcEndpoint is not null && _otlpHttpEndpoint is not null && _collector is not null;

    /// <summary>
    /// Gets whether the SigNoz UI HTTP endpoint and env have been configured (always true after <c>AddSigNoz</c>).
    /// </summary>
    internal bool UiConfigured { get; set; }

    internal int? PreferredUiPort { get; set; }

    internal SigNozUiCredentials UiCredentials { get; private set; } = new();

    internal bool UiCredentialsCustomized { get; set; }

    internal string? UiStoreVolumeSuffix { get; set; }

    internal SigNozOptions? Options { get; private set; }

    internal ReferenceExpression? ClickHouseDsnExpression { get; private set; }

    internal ParameterResource? JwtSecretParameter { get; private set; }

    /// <summary>
    /// Resource builder for the OTLP collector (WaitFor / advanced wiring).
    /// </summary>
    internal IResourceBuilder<ContainerResource> Collector =>
        _collector
        ?? throw new InvalidOperationException(
            "Collector resource is not bound. Call AddSigNoz before reading collector endpoints.");

    internal IResourceBuilder<ContainerResource> ClickHouse =>
        _clickHouse
        ?? throw new InvalidOperationException(
            "ClickHouse resource is not bound. Call AddSigNoz before configuring data volumes.");

    internal IResourceBuilder<ContainerResource> ZooKeeper =>
        _zooKeeper
        ?? throw new InvalidOperationException(
            "ZooKeeper resource is not bound. Call AddSigNoz before configuring data volumes.");

    internal IResourceBuilder<ContainerResource> SchemaMigrator =>
        _schemaMigrator
        ?? throw new InvalidOperationException(
            "Schema migrator resource is not bound. Call AddSigNoz before configuring the UI.");

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Host", ReferenceExpression.Create($"{Host}"));
        yield return new("Port", ReferenceExpression.Create($"{Port}"));
        yield return new("Uri", UriExpression);
    }

    internal void BindClickHouse(IResourceBuilder<ContainerResource> clickHouse)
    {
        ArgumentNullException.ThrowIfNull(clickHouse);
        _clickHouse = clickHouse;
    }

    internal void BindZooKeeper(IResourceBuilder<ContainerResource> zooKeeper)
    {
        ArgumentNullException.ThrowIfNull(zooKeeper);
        _zooKeeper = zooKeeper;
    }

    internal void BindCollector(
        EndpointReference otlpGrpc,
        EndpointReference otlpHttp,
        IResourceBuilder<ContainerResource> collector)
    {
        ArgumentNullException.ThrowIfNull(otlpGrpc);
        ArgumentNullException.ThrowIfNull(otlpHttp);
        ArgumentNullException.ThrowIfNull(collector);

        _otlpGrpcEndpoint = otlpGrpc;
        _otlpHttpEndpoint = otlpHttp;
        _collector = collector;
    }

    internal void BindBackend(
        IResourceBuilder<ContainerResource> zookeeper,
        IResourceBuilder<ContainerResource> migrator,
        ReferenceExpression clickHouseDsn,
        SigNozOptions options,
        IResourceBuilder<ParameterResource>? jwtSecret)
    {
        ArgumentNullException.ThrowIfNull(zookeeper);
        ArgumentNullException.ThrowIfNull(migrator);
        ArgumentNullException.ThrowIfNull(clickHouseDsn);
        ArgumentNullException.ThrowIfNull(options);

        _ = zookeeper;
        _schemaMigrator = migrator;
        ClickHouseDsnExpression = clickHouseDsn;
        Options = options;
        JwtSecretParameter = jwtSecret?.Resource;
        UiCredentials = options.UiCredentials.Clone();
        UiCredentials.Validate();
        UiCredentialsCustomized = !options.UiCredentials.IsDefault();
    }
}

