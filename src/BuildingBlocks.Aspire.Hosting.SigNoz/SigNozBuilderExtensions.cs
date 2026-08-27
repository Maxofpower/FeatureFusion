using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Internal;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding SigNoz resources to an <see cref="IDistributedApplicationBuilder"/>.
/// </summary>
/// <remarks>
/// Method parameters for ports/secrets, fluent <c>With*</c> for volumes and UI host-port override,
/// and OTLP wiring via <see cref="WithSigNozOtlpExporter"/> (not <c>WithReference</c>).
/// </remarks>
public static class SigNozBuilderExtensions
{
    private const string ClickHouseTcpEndpointName = "tcp";
    private const string CollectorHealthEndpointName = "health";
    private const int CollectorHealthTargetPort = 13133;
    private const string DefaultJwtSecret = "local-dev-only-change-me";

    /// <summary>
    /// Unix LF script for Alpine <c>/bin/sh -c</c>. Must not contain CR: Windows CRLF in a raw string
    /// makes BusyBox report <c>set: illegal option -</c>.
    /// </summary>
    internal static string HistogramQuantileInstallScript { get; } =
        string.Join(
            '\n',
            "set -eu",
            "dest=/var/lib/clickhouse/user_scripts/histogramQuantile",
            "mkdir -p /var/lib/clickhouse/user_scripts",
            "if [ -x \"$dest\" ]; then",
            "  echo histogramQuantile already present",
            "  exit 0",
            "fi",
            $"version={SigNozContainerImageTags.HistogramQuantileVersion}",
            "node_os=$(uname -s | tr '[:upper:]' '[:lower:]')",
            "node_arch=$(uname -m | sed 's/aarch64/arm64/' | sed 's/x86_64/amd64/')",
            "apk add --no-cache curl tar gzip",
            "cd /tmp",
            "curl -fsSL -o histogram-quantile.tar.gz \"https://github.com/SigNoz/signoz/releases/download/histogram-quantile%2F${version}/histogram-quantile_${node_os}_${node_arch}.tar.gz\"",
            "tar -xzf histogram-quantile.tar.gz",
            "chmod +x histogram-quantile",
            "mv histogram-quantile \"$dest\"",
            "echo histogramQuantile installed");

    /// <summary>
    /// Adds a local-dev SigNoz stack using only a configure callback (convenience overload).
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="configure">Callback for image tags, lifetime, ports, and collector config path.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/> for <see cref="SigNozResource"/>.</returns>
    /// <remarks>
    /// Prefer the primary overload with explicit <c>port</c> / <c>otlpGrpcPort</c> / <c>jwtSecret</c> parameters
    /// when those values are known at the call site (Aspire AppHost hosting style).
    /// </remarks>
    public static IResourceBuilder<SigNozResource> AddSigNoz(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        Action<SigNozOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.AddSigNoz(name, port: null, otlpGrpcPort: null, otlpHttpPort: null, jwtSecret: null, configure);
    }

    /// <summary>
    /// Adds a local-dev SigNoz stack (ZooKeeper, ClickHouse, schema migrator, OTLP collector, and query UI).
    /// The primary <c>signoz</c> resource exposes an HTTP endpoint for the Aspire dashboard.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource. Used for container name prefixes and parent relationships.</param>
    /// <param name="port">Optional host port for the SigNoz UI HTTP endpoint (container 8080).</param>
    /// <param name="otlpGrpcPort">Optional host port bound to collector OTLP gRPC (container 4317).</param>
    /// <param name="otlpHttpPort">Optional host port bound to collector OTLP HTTP (container 4318).</param>
    /// <param name="jwtSecret">
    /// Optional parameter for <c>SIGNOZ_JWT_SECRET</c>.
    /// If <see langword="null"/>, a local-dev default is used (not for production).
    /// </param>
    /// <param name="configure">Optional callback for image tags, lifetime, and collector config path.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/> for <see cref="SigNozResource"/>.</returns>
    /// <remarks>
    /// <para>
    /// This version of the package defaults to the pinned SigNoz / ClickHouse / collector image tags
    /// shipped with the package. The entire stack is excluded from the Aspire publish manifest (local-dev only).
    /// </para>
    /// <para>
    /// Wire projects with <see cref="WithSigNozOtlpExporter"/> - do not rely on <c>WithReference</c> for OTLP.
    /// Call <see cref="WithDataVolume"/> / <see cref="WithDataBindMount"/> only when you need durable telemetry storage across AppHost restarts.
    /// Use <see cref="WithUi(IResourceBuilder{SigNozResource}, int?, string?, string?, string?, string?)"/> only to override the UI host port and credentials after add.
    /// </para>
    /// <para>
    /// <c>{name}-schema-migrator</c> is a one-shot Session job (collector image:
    /// <c>migrate bootstrap/sync/async</c>) that creates ClickHouse schemas before the collector starts;
    /// an Exited (0) row in the dashboard is expected success.
    /// Both the collector and the query UI wait for it to complete, so the UI never queries a
    /// ClickHouse that is missing <c>signoz_traces</c> / <c>signoz_metadata</c> tables.
    /// </para>
    /// <para>
    /// <c>{name}-clickhouse-udf</c> is a one-shot Session job that installs SigNoz's
    /// <c>histogramQuantile</c> ClickHouse UDF (required for dashboard P95/P99 histogram tiles).
    /// ClickHouse waits for it to complete before starting.
    /// </para>
    /// <example>
    /// <code>
    /// var signoz = builder.AddSigNoz("signoz", port: 8080, otlpGrpcPort: 4317);
    ///
    /// builder.AddProject&lt;Projects.Api&gt;("api")
    ///     .WithSigNozOtlpExporter(signoz);
    /// </code>
    /// </example>
    /// </remarks>
    public static IResourceBuilder<SigNozResource> AddSigNoz(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? port = null,
        int? otlpGrpcPort = null,
        int? otlpHttpPort = null,
        IResourceBuilder<ParameterResource>? jwtSecret = null,
        Action<SigNozOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var options = new SigNozOptions
        {
            UiPort = port,
            OtlpGrpcPort = otlpGrpcPort,
            OtlpHttpPort = otlpHttpPort,
        };
        configure?.Invoke(options);

        // Method parameters win over configure callback (Aspire AppHost hosting style).
        if (port is not null)
        {
            options.UiPort = port;
        }

        if (otlpGrpcPort is not null)
        {
            options.OtlpGrpcPort = otlpGrpcPort;
        }

        if (otlpHttpPort is not null)
        {
            options.OtlpHttpPort = otlpHttpPort;
        }

        ValidateOptions(options);
        ValidateOptionalPort(options.UiPort, nameof(port));
        ValidateOptionalPort(options.OtlpGrpcPort, nameof(otlpGrpcPort));
        ValidateOptionalPort(options.OtlpHttpPort, nameof(otlpHttpPort));

        var zookeeper = builder.AddContainer($"{name}-zookeeper", options.ZooKeeperImage, options.ZooKeeperTag)
            .WithImageRegistry(SigNozContainerImageTags.Registry)
            .WithEndpoint(targetPort: 2181, name: "tcp")
            .WithEnvironment("ALLOW_ANONYMOUS_LOGIN", "yes")
            .WithEnvironment("ZOO_SERVER_ID", "1")
            .WithEnvironment("ZOO_AUTOPURGE_INTERVAL", "1")
            .WithLifetime(options.Lifetime)
            .ExcludeFromManifest();

        var clusterConfigPath = SigNozConfigMaterializer.MaterializeClickHouseClusterConfig(
            zooKeeperHost: $"{name}-zookeeper",
            clickHouseHost: $"{name}-clickhouse");
        var histogramFunctionPath = SigNozConfigMaterializer.MaterializeClickHouseHistogramFunctionConfig();
        var histogramUdfServerConfigPath = SigNozConfigMaterializer.MaterializeClickHouseHistogramUdfServerConfig();

        var clickhouse = builder.AddContainer($"{name}-clickhouse", options.ClickHouseImage, options.ClickHouseTag)
            .WithImageRegistry(SigNozContainerImageTags.Registry)
            .WithEndpoint(targetPort: 9000, name: ClickHouseTcpEndpointName)
            .WithHttpEndpoint(targetPort: 8123, name: "http")
            .WithEnvironment("CLICKHOUSE_SKIP_USER_SETUP", "1")
            .WithBindMount(clusterConfigPath, "/etc/clickhouse-server/config.d/cluster.xml", isReadOnly: true)
            .WithBindMount(histogramFunctionPath, SigNozResource.ClickHouseHistogramFunctionPath, isReadOnly: true)
            .WithBindMount(histogramUdfServerConfigPath, SigNozResource.ClickHouseHistogramUdfConfigPath, isReadOnly: true)
            .WithHttpHealthCheck("/ping", endpointName: "http")
            .WithLifetime(options.Lifetime)
            .ExcludeFromManifest();

        var udfVolume = VolumeNameGenerator.Generate(clickhouse, "udf");
        var udfInit = builder.AddContainer($"{name}-clickhouse-udf", SigNozContainerImageTags.UdfInitImage, SigNozContainerImageTags.UdfInitTag)
            .WithImageRegistry(SigNozContainerImageTags.Registry)
            .WithVolume(udfVolume, SigNozResource.ClickHouseUserScriptsDirectory)
            .WithEntrypoint("/bin/sh")
            .WithArgs("-c", HistogramQuantileInstallScript)
            .WithLifetime(ContainerLifetime.Session)
            .ExcludeFromManifest();

        clickhouse
            .WithVolume(udfVolume, SigNozResource.ClickHouseUserScriptsDirectory)
            .WaitForCompletion(udfInit)
            .WaitFor(zookeeper);

        var clickhouseTcp = clickhouse.GetEndpoint(ClickHouseTcpEndpointName);
        var clickhouseDsnLiteral = $"tcp://{clickhouse.Resource.Name}:9000";
        var clickhouseDsnExpression = ReferenceExpression.Create(
            $"tcp://{clickhouseTcp.Property(EndpointProperty.HostAndPort)}");

        var migrator = builder.AddContainer($"{name}-schema-migrator", options.SchemaMigratorImage, options.SchemaMigratorTag)
            .WithImageRegistry(SigNozContainerImageTags.Registry)
            .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_CLICKHOUSE_DSN", clickhouseDsnLiteral)
            .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_TIMEOUT", "10m")
            // Image ENTRYPOINT is signoz-otel-collector; override so we can chain migrate subcommands.
            .WithEntrypoint("/bin/sh")
            .WithArgs(
                "-c",
                "/signoz-otel-collector migrate bootstrap && /signoz-otel-collector migrate sync up && /signoz-otel-collector migrate async up")
            .WithLifetime(ContainerLifetime.Session)
            .WaitFor(clickhouse)
            .ExcludeFromManifest();

        var configPath = SigNozConfigMaterializer.MaterializeCollectorConfig(
            name,
            clickhouseDsnLiteral,
            options.CollectorConfigPath);

        var collector = builder.AddContainer($"{name}-otel-collector", options.CollectorImage, options.CollectorTag)
            .WithImageRegistry(SigNozContainerImageTags.Registry)
            .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_CLICKHOUSE_DSN", clickhouseDsnLiteral)
            .WithEnvironment("SIGNOZ_OTEL_COLLECTOR_TIMEOUT", "10m")
            .WithEntrypoint("/bin/sh")
            .WithArgs(
                "-c",
                "/signoz-otel-collector migrate sync check && /signoz-otel-collector --config=/etc/otel-collector-config.yaml")
            .WithBindMount(configPath, "/etc/otel-collector-config.yaml", isReadOnly: true)
            .WithEndpoint(port: options.OtlpGrpcPort, targetPort: 4317, name: SigNozResource.OtlpGrpcEndpointName)
            .WithEndpoint(port: options.OtlpHttpPort, targetPort: 4318, name: SigNozResource.OtlpHttpEndpointName)
            .WithHttpEndpoint(targetPort: CollectorHealthTargetPort, name: CollectorHealthEndpointName)
            .WithHttpHealthCheck("/", endpointName: CollectorHealthEndpointName)
            .WithLifetime(options.Lifetime)
            .WaitForCompletion(migrator)
            .WaitFor(clickhouse)
            .ExcludeFromManifest();

        var signozResource = new SigNozResource(name);
        signozResource.BindClickHouse(clickhouse);
        signozResource.BindZooKeeper(zookeeper);
        signozResource.BindCollector(
            collector.GetEndpoint(SigNozResource.OtlpGrpcEndpointName),
            collector.GetEndpoint(SigNozResource.OtlpHttpEndpointName),
            collector);
        signozResource.BindBackend(zookeeper, migrator, clickhouseDsnExpression, options, jwtSecret);
        signozResource.PreferredUiPort = options.UiPort;

        var signoz = builder.AddResource(signozResource)
            .WithImage(options.SigNozImage, options.SigNozTag)
            .WithImageRegistry(SigNozContainerImageTags.Registry)
            .WithIconName("Pulse")
            .WithLifetime(options.Lifetime)
            .ExcludeFromManifest();

        ConfigureUi(signoz);

        zookeeper.WithParentRelationship(signoz);
        clickhouse.WithParentRelationship(signoz);
        udfInit.WithParentRelationship(signoz);
        migrator.WithParentRelationship(signoz);
        collector.WithParentRelationship(signoz);

        return signoz;
    }

    /// <summary>
    /// Overrides the host port for the SigNoz query UI HTTP endpoint and optionally configures local-dev admin credentials.
    /// </summary>
    /// <param name="builder">The SigNoz resource builder.</param>
    /// <param name="port">Host port for the UI HTTP endpoint (container 8080). When <see langword="null"/>, leaves the current port unchanged.</param>
    /// <param name="adminEmail">Admin email for UI login and dashboard seeding. Default: <see cref="SigNozUiCredentials.DefaultEmail"/>.</param>
    /// <param name="adminPassword">Admin password for UI login and dashboard seeding. Default: <see cref="SigNozUiCredentials.DefaultPassword"/>.</param>
    /// <param name="adminName">Display name for first-run admin registration. Default: <see cref="SigNozUiCredentials.DefaultAdminName"/>.</param>
    /// <param name="orgName">Organization name for first-run admin registration. Default: <see cref="SigNozUiCredentials.DefaultOrgName"/>.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="AddSigNoz(IDistributedApplicationBuilder, string, int?, int?, int?, IResourceBuilder{ParameterResource}?, Action{SigNozOptions}?)"/> already configures the UI with an HTTP endpoint for the Aspire dashboard.
    /// Call this to override the host port and/or local-dev login credentials used by <see cref="WithDashboards"/>.
    /// Each distinct <paramref name="adminEmail"/> gets its own sqlite volume so first-run registration works
    /// without wiping Docker volumes when credentials change. SigNoz root-user env vars are applied so the UI
    /// does not show the first-run signup wizard when credentials are configured here.
    /// <paramref name="adminPassword"/> must satisfy SigNoz root-user rules (≥12 chars, upper, lower, digit, symbol).
    /// Default credentials are exposed on the Aspire resource connection panel; custom credentials are not.
    /// </para>
    /// <example>
    /// <code>
    /// var signoz = builder.AddSigNoz("signoz")
    ///     .WithUi(port: 8080, adminEmail: "dev@local.test", adminPassword: "DevPassword123!");
    /// </code>
    /// </example>
    /// </remarks>
    public static IResourceBuilder<SigNozResource> WithUi(
        this IResourceBuilder<SigNozResource> builder,
        int? port = null,
        string? adminEmail = null,
        string? adminPassword = null,
        string? adminName = null,
        string? orgName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateOptionalPort(port, nameof(port));

        if (!builder.Resource.UiConfigured)
        {
            ConfigureUi(builder);
        }

        ApplyUiCredentials(builder.Resource, adminEmail, adminPassword, adminName, orgName);
        EnsureUiSqliteVolume(builder);
        EnsureRootUserEnvironment(builder);

        if (port is null)
        {
            return builder;
        }

        builder.Resource.PreferredUiPort = port;

        if (builder.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out var endpoints))
        {
            var http = endpoints.FirstOrDefault(e =>
                string.Equals(e.Name, SigNozResource.PrimaryEndpointName, StringComparison.OrdinalIgnoreCase));
            if (http is not null)
            {
                http.Port = port;
            }
        }

        return builder;
    }

    private static void ApplyUiCredentials(
        SigNozResource resource,
        string? adminEmail,
        string? adminPassword,
        string? adminName,
        string? orgName)
    {
        if (adminEmail is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(adminEmail);
            resource.UiCredentials.AdminEmail = adminEmail;
            resource.UiCredentialsCustomized = true;
        }

        if (adminPassword is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(adminPassword);
            resource.UiCredentials.AdminPassword = adminPassword;
            resource.UiCredentialsCustomized = true;
        }

        if (adminName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(adminName);
            resource.UiCredentials.AdminName = adminName;
            resource.UiCredentialsCustomized = true;
        }

        if (orgName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(orgName);
            resource.UiCredentials.OrgName = orgName;
            resource.UiCredentialsCustomized = true;
        }

        resource.UiCredentials.Validate();
    }

    private static void EnsureRootUserEnvironment(IResourceBuilder<SigNozResource> builder)
    {
        var credentials = builder.Resource.UiCredentials;
        builder
            .WithEnvironment("SIGNOZ_USER_ROOT_ENABLED", "true")
            .WithEnvironment("SIGNOZ_USER_ROOT_EMAIL", credentials.AdminEmail)
            .WithEnvironment("SIGNOZ_USER_ROOT_PASSWORD", credentials.AdminPassword)
            .WithEnvironment("SIGNOZ_USER_ROOT_ORG_NAME", credentials.OrgName);

        EnsureDefaultCredentialConnectionProperties(builder);
    }

    private static void EnsureDefaultCredentialConnectionProperties(IResourceBuilder<SigNozResource> builder)
    {
        RemoveAdminConnectionProperties(builder.Resource);

        if (builder.Resource.UiCredentialsCustomized)
        {
            return;
        }

        var credentials = builder.Resource.UiCredentials;
        builder
            .WithConnectionProperty("AdminEmail", credentials.AdminEmail)
            .WithConnectionProperty("AdminPassword", credentials.AdminPassword);
    }

    private static void RemoveAdminConnectionProperties(IResource resource)
    {
        if (!resource.TryGetAnnotationsOfType<ConnectionPropertyAnnotation>(out var props))
        {
            return;
        }

        foreach (var prop in props.Where(p => p.Name is "AdminEmail" or "AdminPassword").ToList())
        {
            resource.Annotations.Remove(prop);
        }
    }

    /// <summary>
    /// Adds named volumes for ClickHouse and ZooKeeper data directories.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The ClickHouse volume name. Defaults to an auto-generated name based on the application and resource names.</param>
    /// <param name="isReadOnly">Whether the volume is mounted read-only.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// SigNoz ClickHouse tables are replicated and require matching ZooKeeper metadata. Persist both
    /// directories together; persisting ClickHouse alone causes readonly tables after restart.
    /// </remarks>
    public static IResourceBuilder<SigNozResource> WithDataVolume(
        this IResourceBuilder<SigNozResource> builder,
        string? name = null,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var clickHouse = builder.Resource.ClickHouse;
        clickHouse.WithVolume(
            name ?? VolumeNameGenerator.Generate(clickHouse, "data"),
            SigNozResource.ClickHouseDataDirectory,
            isReadOnly);

        var zooKeeper = builder.Resource.ZooKeeper;
        zooKeeper.WithVolume(
            VolumeNameGenerator.Generate(zooKeeper, "data"),
            SigNozResource.ZooKeeperDataDirectory,
            isReadOnly);

        return builder;
    }

    /// <summary>
    /// Adds bind mounts for the ClickHouse and ZooKeeper data directories.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The host directory mounted at ClickHouse <c>/var/lib/clickhouse</c>. ZooKeeper is mounted from <c>{source}/zookeeper</c>.</param>
    /// <param name="isReadOnly">Whether the mounts are read-only.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// SigNoz ClickHouse tables are replicated and require matching ZooKeeper metadata. Persist both
    /// directories together; persisting ClickHouse alone causes readonly tables after restart.
    /// </remarks>
    public static IResourceBuilder<SigNozResource> WithDataBindMount(
        this IResourceBuilder<SigNozResource> builder,
        string source,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        builder.Resource.ClickHouse.WithBindMount(source, SigNozResource.ClickHouseDataDirectory, isReadOnly);
        builder.Resource.ZooKeeper.WithBindMount(
            Path.Combine(source, "zookeeper"),
            SigNozResource.ZooKeeperDataDirectory,
            isReadOnly);
        return builder;
    }

    /// <summary>
    /// Seeds local-dev dashboards into the SigNoz UI after it is healthy (ASP.NET Core metrics + BuildingBlocks overview).
    /// The admin account is provisioned at container startup via SigNoz root-user env vars from <see cref="WithUi"/>.
    /// Idempotent by dashboard title.
    /// </summary>
    /// <param name="builder">The SigNoz resource builder.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// Admin credentials come from <see cref="WithUi"/> or <see cref="SigNozOptions.UiCredentials"/>.
    /// Defaults: <see cref="SigNozUiCredentials.DefaultEmail"/> / <see cref="SigNozUiCredentials.DefaultPassword"/>.
    /// Requires SigNoz UI ≥ 0.135 (Dashboards V2). Failures are best-effort and do not stop the AppHost.
    /// </remarks>
    public static IResourceBuilder<SigNozResource> WithDashboards(
        this IResourceBuilder<SigNozResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.OnResourceReady(async (resource, _, cancellationToken) =>
        {
            if (resource is not SigNozResource signoz)
            {
                return;
            }

            var baseAddress = SigNozDashboardSeeder.ResolveUiBaseAddress(signoz);
            if (baseAddress is null)
            {
                return;
            }

            try
            {
                await SigNozDashboardSeeder.SeedAsync(baseAddress, signoz.UiCredentials, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Local-dev convenience only — do not fail the distributed application.
            }
        });

        return builder;
    }

    /// <summary>
    /// Points a project at the local SigNoz OTLP collector by setting
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> and <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>.
    /// </summary>
    /// <param name="project">The project resource builder.</param>
    /// <param name="signoz">The SigNoz resource builder from <c>AddSigNoz</c>.</param>
    /// <param name="protocol">OTLP transport; default gRPC.</param>
    /// <returns>The project <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// Intended for Aspire local development. Overrides the Aspire dashboard OTLP endpoint for the project.
    /// Does not <c>WaitFor</c> the collector: Aspire's healthy-wait can block the app while the collector
    /// is already accepting OTLP; exporters retry on connect. Production apps should use
    /// <c>BuildingBlocks.Telemetry</c> with a real OTLP backend - not this helper.
    /// </remarks>
    public static IResourceBuilder<ProjectResource> WithSigNozOtlpExporter(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<SigNozResource> signoz,
        SigNozOtlpProtocol protocol = SigNozOtlpProtocol.Grpc)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(signoz);

        if (!signoz.Resource.IsCollectorBound)
        {
            throw new InvalidOperationException(
                "SigNoz collector endpoints are not bound. Ensure the resource was created with AddSigNoz.");
        }

        var endpoint = protocol == SigNozOtlpProtocol.HttpProtobuf
            ? signoz.Resource.OtlpHttpEndpoint
            : signoz.Resource.OtlpGrpcEndpoint;

        var protocolValue = protocol == SigNozOtlpProtocol.HttpProtobuf ? "http/protobuf" : "grpc";

        // OTLP always uses http:// to the allocated host port. Do not use EndpointProperty.Scheme —
        // the collector gRPC endpoint is registered without an HTTP scheme annotation, so Scheme
        // can be empty and produce an invalid URL. Do not use Url (may be a Docker DNS name).
        return project
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] =
                    ReferenceExpression.Create(
                        $"http://{endpoint.Property(EndpointProperty.Host)}:{endpoint.Property(EndpointProperty.Port)}");
                context.EnvironmentVariables["OTEL_EXPORTER_OTLP_PROTOCOL"] = protocolValue;
            });
    }

    private static void ConfigureUi(IResourceBuilder<SigNozResource> builder)
    {
        var resource = builder.Resource;
        if (resource.UiConfigured)
        {
            return;
        }

        var clickhouseDsn = resource.ClickHouseDsnExpression
            ?? throw new InvalidOperationException("ClickHouse DSN is not bound. Call AddSigNoz first.");

        // The UI hosts the query-service, which reads signoz_traces/_logs/_metrics/_metadata directly.
        // Without WaitForCompletion(migrator) it can serve pages before migrate created those tables, and
        // ClickHouse answers with "code: 60 Unknown table expression identifier" (for example
        // signoz_metadata.distributed_column_evolution_metadata on Instrumentation / Traces).
        // Still no WaitFor on collector HTTP health — that can leave the UI stuck Waiting while the
        // collector is already Up/OTLP-ready.
        builder
            .WithHttpEndpoint(port: resource.PreferredUiPort, targetPort: 8080, name: SigNozResource.PrimaryEndpointName)
            .WithEnvironment("SIGNOZ_TELEMETRYSTORE_CLICKHOUSE_DSN", clickhouseDsn)
            .WithEnvironment("SIGNOZ_ALERTMANAGER_PROVIDER", "signoz")
            .WithEnvironment("SIGNOZ_SQLSTORE_SQLITE_PATH", "/var/lib/signoz/signoz.db")
            .WithHttpHealthCheck("/api/v1/health", endpointName: SigNozResource.PrimaryEndpointName)
            .WaitFor(resource.ClickHouse)
            .WaitForCompletion(resource.SchemaMigrator);

        if (resource.JwtSecretParameter is { } jwtParam)
        {
            builder.WithEnvironment("SIGNOZ_JWT_SECRET", jwtParam);
        }
        else
        {
            builder.WithEnvironment("SIGNOZ_JWT_SECRET", DefaultJwtSecret);
        }

        EnsureUiSqliteVolume(builder);
        EnsureRootUserEnvironment(builder);
        resource.UiConfigured = true;
    }

    private static void EnsureUiSqliteVolume(IResourceBuilder<SigNozResource> builder)
    {
        var resource = builder.Resource;
        var suffix = SigNozUiStore.GetVolumeSuffix(resource.UiCredentials);
        if (string.Equals(resource.UiStoreVolumeSuffix, suffix, StringComparison.Ordinal))
        {
            return;
        }

        if (resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var mounts))
        {
            foreach (var mount in mounts.Where(m => m.Target == SigNozUiStore.MountPath).ToList())
            {
                resource.Annotations.Remove(mount);
            }
        }

        builder.WithVolume(
            VolumeNameGenerator.Generate(builder, $"sqlite-{suffix}"),
            SigNozUiStore.MountPath);
        resource.UiStoreVolumeSuffix = suffix;
    }

    private static void ValidateOptions(SigNozOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClickHouseImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClickHouseTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ZooKeeperImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ZooKeeperTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CollectorImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CollectorTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SchemaMigratorImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SchemaMigratorTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SigNozImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SigNozTag);
    }

    private static void ValidateOptionalPort(int? port, string paramName)
    {
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(paramName, port, "Port must be 1-65535 when specified.");
        }
    }
}
