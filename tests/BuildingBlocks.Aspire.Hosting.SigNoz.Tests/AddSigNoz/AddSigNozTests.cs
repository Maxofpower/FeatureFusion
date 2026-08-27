using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Internal;
using Xunit;

namespace BuildingBlocks.Aspire.Hosting.SigNoz.Tests.AddSigNoz;

public sealed class AddSigNozTests
{
    [Fact]
    public void Registers_collector_stack_and_always_configures_ui()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        Assert.Equal("signoz", signoz.Resource.Name);
        Assert.True(signoz.Resource.IsCollectorBound);
        Assert.True(signoz.Resource.UiConfigured);
        Assert.NotNull(signoz.Resource.OtlpGrpcEndpoint);
        Assert.NotNull(signoz.Resource.OtlpHttpEndpoint);
        Assert.NotNull(signoz.Resource.CollectorResource);
        Assert.NotNull(signoz.Resource.PrimaryEndpoint);

        Assert.True(signoz.Resource.TryGetEndpoints(out var endpoints));
        Assert.Contains(endpoints, e => e.Name == SigNozResource.PrimaryEndpointName);

        Assert.Contains(builder.Resources, r => r.Name == "signoz-clickhouse");
        Assert.Contains(builder.Resources, r => r.Name == "signoz-zookeeper");
        Assert.Contains(builder.Resources, r => r.Name == "signoz-otel-collector");
        Assert.Contains(builder.Resources, r => r.Name == "signoz-schema-migrator");
        Assert.Contains(builder.Resources, r => r.Name == "signoz-clickhouse-udf");
        Assert.Contains(builder.Resources, r => r.Name == "signoz");
    }

    [Fact]
    public void AddSigNoz_always_registers_http_endpoint_without_WithUi()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        Assert.True(signoz.Resource.UiConfigured);
        Assert.True(signoz.Resource.TryGetEndpoints(out var endpoints));
        var http = Assert.Single(endpoints, e => e.Name == SigNozResource.PrimaryEndpointName);
        Assert.Equal(8080, http.TargetPort);
    }

    [Fact]
    public void UI_env_includes_alertmanager_sqlite_and_jwt()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        var env = GetEnvironmentVariables(signoz.Resource);

        Assert.True(env.TryGetValue("SIGNOZ_ALERTMANAGER_PROVIDER", out var alertProvider));
        Assert.Equal("signoz", alertProvider);
        Assert.True(env.TryGetValue("SIGNOZ_SQLSTORE_SQLITE_PATH", out var sqlitePath));
        Assert.Equal("/var/lib/signoz/signoz.db", sqlitePath);
        Assert.True(env.TryGetValue("SIGNOZ_JWT_SECRET", out var jwt));
        Assert.Equal("local-dev-only-change-me", jwt);
        Assert.True(env.ContainsKey("SIGNOZ_TELEMETRYSTORE_CLICKHOUSE_DSN"));
        Assert.True(env.TryGetValue("SIGNOZ_USER_ROOT_ENABLED", out var rootEnabled));
        Assert.Equal("true", rootEnabled);
        Assert.True(env.TryGetValue("SIGNOZ_USER_ROOT_EMAIL", out var rootEmail));
        Assert.Equal(SigNozUiCredentials.DefaultEmail, rootEmail);
        Assert.True(env.TryGetValue("SIGNOZ_USER_ROOT_PASSWORD", out var rootPassword));
        Assert.Equal(SigNozUiCredentials.DefaultPassword, rootPassword);
        Assert.True(env.TryGetValue("SIGNOZ_USER_ROOT_ORG_NAME", out var rootOrg));
        Assert.Equal(SigNozUiCredentials.DefaultOrgName, rootOrg);
    }

    [Fact]
    public void UI_waits_for_clickhouse_not_collector()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        var clickhouse = Assert.Single(builder.Resources, r => r.Name == "signoz-clickhouse");
        var collector = Assert.Single(builder.Resources, r => r.Name == "signoz-otel-collector");

        Assert.True(signoz.Resource.TryGetAnnotationsOfType<WaitAnnotation>(out var waits));
        Assert.Contains(waits, w => ReferenceEquals(w.Resource, clickhouse));
        Assert.DoesNotContain(waits, w => ReferenceEquals(w.Resource, collector));
    }

    [Fact]
    public void UI_waits_for_schema_migrator_completion()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        var migrator = Assert.Single(builder.Resources, r => r.Name == "signoz-schema-migrator");

        Assert.True(signoz.Resource.TryGetAnnotationsOfType<WaitAnnotation>(out var waits));
        Assert.Contains(
            waits,
            w => ReferenceEquals(w.Resource, migrator) && w.WaitType == WaitType.WaitForCompletion);
    }

    [Fact]
    public void Schema_migrator_is_session_and_collector_waits_for_completion()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddSigNoz("signoz");

        var migrator = Assert.Single(builder.Resources, r => r.Name == "signoz-schema-migrator");
        Assert.True(migrator.TryGetAnnotationsOfType<ContainerLifetimeAnnotation>(out var lifetimes));
        Assert.Contains(lifetimes, l => l.Lifetime == ContainerLifetime.Session);

        var collector = Assert.Single(builder.Resources, r => r.Name == "signoz-otel-collector");
        Assert.True(collector.TryGetAnnotationsOfType<WaitAnnotation>(out var waits));
        Assert.Contains(
            waits,
            w => ReferenceEquals(w.Resource, migrator) && w.WaitType == WaitType.WaitForCompletion);
    }

    [Fact]
    public void ClickHouse_mounts_histogram_udf_and_waits_for_init()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddSigNoz("signoz");

        var udfInit = Assert.Single(builder.Resources, r => r.Name == "signoz-clickhouse-udf");
        Assert.True(udfInit.TryGetAnnotationsOfType<ContainerLifetimeAnnotation>(out var lifetimes));
        Assert.Contains(lifetimes, l => l.Lifetime == ContainerLifetime.Session);

        var clickhouse = Assert.Single(builder.Resources, r => r.Name == "signoz-clickhouse");
        Assert.True(clickhouse.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var mounts));
        Assert.Contains(mounts, m => m.Target == SigNozResource.ClickHouseHistogramFunctionPath);
        Assert.Contains(mounts, m => m.Target == SigNozResource.ClickHouseHistogramUdfConfigPath);
        Assert.Contains(mounts, m => m.Target == SigNozResource.ClickHouseUserScriptsDirectory);

        Assert.True(clickhouse.TryGetAnnotationsOfType<WaitAnnotation>(out var waits));
        Assert.Contains(
            waits,
            w => ReferenceEquals(w.Resource, udfInit) && w.WaitType == WaitType.WaitForCompletion);

        var xml = File.ReadAllText(SigNozConfigMaterializer.MaterializeClickHouseHistogramFunctionConfig());
        Assert.Contains("histogramQuantile", xml, StringComparison.Ordinal);
        Assert.Contains("<format>CSV</format>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("TabSeparated", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Histogram_udf_install_script_is_unix_lf()
    {
        var script = SigNozBuilderExtensions.HistogramQuantileInstallScript;

        Assert.DoesNotContain('\r', script);
        Assert.StartsWith("set -eu\n", script, StringComparison.Ordinal);
        Assert.Contains($"version={SigNozContainerImageTags.HistogramQuantileVersion}", script, StringComparison.Ordinal);
        Assert.Contains("histogramQuantile", script, StringComparison.Ordinal);
    }

    [Fact]
    public void WithUi_overrides_host_port_on_http_endpoint()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz").WithUi(port: 18080);

        Assert.Equal(18080, signoz.Resource.PreferredUiPort);
        Assert.True(signoz.Resource.TryGetEndpoints(out var endpoints));
        var http = Assert.Single(endpoints, e => e.Name == SigNozResource.PrimaryEndpointName);
        Assert.Equal(18080, http.Port);
    }

    [Fact]
    public void WithUi_sets_admin_credentials()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz").WithUi(
            adminEmail: "dev@local.test",
            adminPassword: "DevPassword123!",
            adminName: "Dev Admin",
            orgName: "DevOrg");

        Assert.Equal("dev@local.test", signoz.Resource.UiCredentials.AdminEmail);
        Assert.Equal("DevPassword123!", signoz.Resource.UiCredentials.AdminPassword);
        Assert.Equal("Dev Admin", signoz.Resource.UiCredentials.AdminName);
        Assert.Equal("DevOrg", signoz.Resource.UiCredentials.OrgName);
    }

    [Fact]
    public void WithUi_uses_default_credentials_when_not_specified()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz").WithUi();

        Assert.Equal(SigNozUiCredentials.DefaultEmail, signoz.Resource.UiCredentials.AdminEmail);
        Assert.Equal(SigNozUiCredentials.DefaultPassword, signoz.Resource.UiCredentials.AdminPassword);
    }

    [Fact]
    public void AddSigNoz_configure_callback_sets_ui_credentials()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz", configure: o =>
        {
            o.UiCredentials.AdminEmail = "opts@local.test";
            o.UiCredentials.AdminPassword = "OptsPassword1!";
        });

        Assert.Equal("opts@local.test", signoz.Resource.UiCredentials.AdminEmail);
        Assert.Equal("OptsPassword1!", signoz.Resource.UiCredentials.AdminPassword);
    }

    [Fact]
    public void WithUi_overrides_credentials_from_options()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz", configure: o =>
        {
            o.UiCredentials.AdminEmail = "opts@local.test";
        }).WithUi(adminEmail: "ui@local.test");

        Assert.Equal("ui@local.test", signoz.Resource.UiCredentials.AdminEmail);
    }

    [Fact]
    public void WithUi_custom_email_uses_distinct_sqlite_volume()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz")
            .WithUi(adminEmail: "dev@local.test");

        Assert.True(signoz.Resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var mounts));
        var suffix = SigNozUiStore.GetVolumeSuffix(signoz.Resource.UiCredentials);
        Assert.Contains(mounts, m => m.Target == SigNozUiStore.MountPath && m.Source!.Contains($"sqlite-{suffix}", StringComparison.Ordinal));
        Assert.Equal(suffix, signoz.Resource.UiStoreVolumeSuffix);
    }

    [Fact]
    public void WithUi_rejects_blank_credentials()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        Assert.Throws<ArgumentException>(() => signoz.WithUi(adminEmail: " "));
        Assert.Throws<ArgumentException>(() => signoz.WithUi(adminPassword: ""));
    }

    [Fact]
    public void Invalid_ports_throw()
    {
        var builder = DistributedApplication.CreateBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddSigNoz("signoz", port: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddSigNoz("bad", otlpGrpcPort: 70000));

        var signoz = builder.AddSigNoz("ok");
        Assert.Throws<ArgumentOutOfRangeException>(() => signoz.WithUi(port: -1));
    }

    [Fact]
    public void Jwt_secret_parameter_is_wired_when_passed()
    {
        var builder = DistributedApplication.CreateBuilder();
        var jwt = builder.AddParameter("signoz-jwt");
        var signoz = builder.AddSigNoz("signoz", jwtSecret: jwt);

        Assert.Same(jwt.Resource, signoz.Resource.JwtSecretParameter);

        var env = GetEnvironmentVariables(signoz.Resource);
        Assert.True(env.ContainsKey("SIGNOZ_JWT_SECRET"));
        Assert.NotEqual("local-dev-only-change-me", env["SIGNOZ_JWT_SECRET"]?.ToString());
    }

    [Fact]
    public void Excludes_entire_stack_from_publish_manifest()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddSigNoz("signoz");

        foreach (var name in new[]
                 {
                     "signoz",
                     "signoz-clickhouse",
                     "signoz-zookeeper",
                     "signoz-otel-collector",
                     "signoz-schema-migrator",
                     "signoz-clickhouse-udf",
                 })
        {
            var resource = Assert.Single(builder.Resources, r => r.Name == name);
            Assert.True(
                resource.TryGetAnnotationsOfType<ManifestPublishingCallbackAnnotation>(out var annotations)
                && annotations.Contains(ManifestPublishingCallbackAnnotation.Ignore),
                $"{name} should be ExcludeFromManifest");
        }
    }

    [Fact]
    public void Applies_options_overrides()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("obs", o =>
        {
            o.SigNozTag = "v0.90.0";
            o.UiPort = 18080;
        });

        Assert.Equal("obs", signoz.Resource.Name);
        Assert.Equal(18080, signoz.Resource.PreferredUiPort);
        Assert.Contains(builder.Resources, r => r.Name == "obs-clickhouse");
    }

    [Fact]
    public void Accepts_explicit_otlp_and_ui_ports()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz", port: 18080, otlpGrpcPort: 14317, otlpHttpPort: 14318);

        Assert.True(signoz.Resource.UiConfigured);
        Assert.Equal(18080, signoz.Resource.PreferredUiPort);

        var collector = Assert.Single(builder.Resources, r => r.Name == "signoz-otel-collector");
        Assert.True(collector.TryGetEndpoints(out var endpoints));
        Assert.Contains(endpoints, e => e.Name == SigNozResource.OtlpGrpcEndpointName && e.Port == 14317);
        Assert.Contains(endpoints, e => e.Name == SigNozResource.OtlpHttpEndpointName && e.Port == 14318);
    }

    [Fact]
    public void Connection_properties_expose_host_port_uri()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        Assert.NotNull(signoz.Resource.ConnectionStringExpression);
        Assert.NotNull(signoz.Resource.UriExpression);

        var props = ((IResourceWithConnectionString)signoz.Resource).GetConnectionProperties()
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(props.ContainsKey("Host"));
        Assert.True(props.ContainsKey("Port"));
        Assert.True(props.ContainsKey("Uri"));
        Assert.True(signoz.Resource.TryGetAnnotationsOfType<ConnectionPropertyAnnotation>(out var connectionProps));
        Assert.Contains(connectionProps, p => p.Name == "AdminEmail");
        Assert.Contains(connectionProps, p => p.Name == "AdminPassword");
    }

    [Fact]
    public void Connection_properties_hide_custom_credentials()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz")
            .WithUi(adminEmail: "dev@local.test", adminPassword: "DevPassword123!");

        var hasAdminProps = signoz.Resource.TryGetAnnotationsOfType<ConnectionPropertyAnnotation>(out var connectionProps)
            && connectionProps.Any(p => p.Name is "AdminEmail" or "AdminPassword");
        Assert.False(hasAdminProps);
    }

    [Fact]
    public void WithUi_applies_root_user_env_for_custom_credentials()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz")
            .WithUi(adminEmail: "dev@local.test", adminPassword: "DevPassword123!", orgName: "DevOrg");

        var env = GetEnvironmentVariables(signoz.Resource);
        Assert.Equal("dev@local.test", env["SIGNOZ_USER_ROOT_EMAIL"]);
        Assert.Equal("DevPassword123!", env["SIGNOZ_USER_ROOT_PASSWORD"]);
        Assert.Equal("DevOrg", env["SIGNOZ_USER_ROOT_ORG_NAME"]);
        Assert.Equal("true", env["SIGNOZ_USER_ROOT_ENABLED"]);
    }

    [Fact]
    public void WithDataVolume_attaches_clickhouse_and_zookeeper_volumes()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz").WithDataVolume();

        var clickhouse = Assert.Single(builder.Resources, r => r.Name == "signoz-clickhouse");
        Assert.True(clickhouse.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var clickhouseMounts));
        Assert.Contains(clickhouseMounts, m => m.Target == SigNozResource.ClickHouseDataDirectory);

        var zookeeper = Assert.Single(builder.Resources, r => r.Name == "signoz-zookeeper");
        Assert.True(zookeeper.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var zookeeperMounts));
        Assert.Contains(zookeeperMounts, m => m.Target == SigNozResource.ZooKeeperDataDirectory);

        Assert.NotNull(signoz);
    }

    [Fact]
    public void WithDataBindMount_attaches_clickhouse_and_zookeeper_host_paths()
    {
        var builder = DistributedApplication.CreateBuilder();
        var source = Path.GetTempPath();
        builder.AddSigNoz("signoz").WithDataBindMount(source);

        var clickhouse = Assert.Single(builder.Resources, r => r.Name == "signoz-clickhouse");
        Assert.True(clickhouse.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var clickhouseMounts));
        Assert.Contains(
            clickhouseMounts,
            m => m.Type == ContainerMountType.BindMount
                 && m.Target == SigNozResource.ClickHouseDataDirectory
                 && m.Source == source);

        var zookeeper = Assert.Single(builder.Resources, r => r.Name == "signoz-zookeeper");
        Assert.True(zookeeper.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var zookeeperMounts));
        Assert.Contains(
            zookeeperMounts,
            m => m.Type == ContainerMountType.BindMount
                 && m.Target == SigNozResource.ZooKeeperDataDirectory
                 && m.Source == Path.Combine(source, "zookeeper"));
    }

    [Fact]
    public void Registers_health_check_and_icon()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        Assert.True(signoz.Resource.TryGetAnnotationsOfType<HealthCheckAnnotation>(out var health));
        Assert.NotEmpty(health);

        Assert.True(signoz.Resource.TryGetAnnotationsOfType<ResourceIconAnnotation>(out var icons));
        Assert.Contains(icons, i => string.Equals(i.IconName, "Pulse", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UI_has_sqlite_volume()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");

        Assert.True(signoz.Resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var mounts));
        Assert.Contains(mounts, m => m.Target == SigNozUiStore.MountPath);
    }

    [Fact]
    public void WithDashboards_registers_resource_ready_subscription()
    {
        var builder = DistributedApplication.CreateBuilder();
        var signoz = builder.AddSigNoz("signoz");
        Assert.False(HasResourceReadySubscriptions(builder));

        signoz.WithDashboards();
        Assert.True(HasResourceReadySubscriptions(builder));
        Assert.Equal("v0.136.1", SigNozContainerImageTags.SigNozTag);
        Assert.Equal("v0.144.6", SigNozContainerImageTags.CollectorTag);
    }

    [Fact]
    public void Default_image_tags_are_pinned_not_latest()
    {
        Assert.False(SigNozContainerImageTags.SigNozTag.Contains("latest", StringComparison.OrdinalIgnoreCase));
        Assert.False(SigNozContainerImageTags.CollectorTag.Contains("latest", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(SigNozContainerImageTags.CollectorImage, SigNozContainerImageTags.SchemaMigratorImage);
        Assert.False(SigNozContainerImageTags.UdfInitTag.Contains("latest", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasResourceReadySubscriptions(IDistributedApplicationBuilder builder)
    {
        var eventing = builder.Eventing;
        var field = eventing.GetType().GetField(
            "_eventSubscriptionListLookup",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field?.GetValue(eventing) is not System.Collections.IDictionary lookup)
        {
            return false;
        }

        foreach (System.Collections.DictionaryEntry entry in lookup)
        {
            if (entry.Key is Type type
                && type.Name == "ResourceReadyEvent"
                && entry.Value is System.Collections.ICollection { Count: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, object> GetEnvironmentVariables(IResource resource)
    {
        var env = new Dictionary<string, object>(StringComparer.Ordinal);
        if (!resource.TryGetAnnotationsOfType<EnvironmentCallbackAnnotation>(out var annotations))
        {
            return env;
        }

        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            env);

        foreach (var annotation in annotations)
        {
            annotation.Callback(context).GetAwaiter().GetResult();
        }

        return env;
    }
}
