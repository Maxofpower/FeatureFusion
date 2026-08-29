# BuildingBlocks.Aspire.Hosting.SigNoz — agent notes

Local-dev Aspire AppHost SigNoz stack. Install: `dotnet add package BuildingBlocks.Aspire.Hosting.SigNoz`. TFM net10.0 (Aspire 13.4). Docker required. Production OTLP: `BuildingBlocks.Telemetry`, not this package.

```csharp
var signoz = builder.AddSigNoz("signoz", port: 8080, otlpGrpcPort: 4317, otlpHttpPort: 4318, jwtSecret: null, configure: o =>
{
    o.Lifetime = ContainerLifetime.Persistent;
    o.CollectorConfigPath = null;
    o.SigNozTag = "v0.136.1";
})
    .WithUi(port: 8080, adminEmail: "dev@local.test", adminPassword: "DevPassword123!", adminName: "Local Admin", orgName: "default")
    .WithDashboards()
    .WithDataVolume();

builder.AddProject<Projects.Api>("api")
    .WithSigNozOtlpExporter(signoz, SigNozOtlpProtocol.Grpc);
```

`WithSigNozOtlpExporter` is `ProjectResource` only (not `WithReference`). Persist ClickHouse **and** ZooKeeper together. Password ≥12 with upper/lower/digit/symbol. Not for production exporters.
