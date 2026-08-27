# BuildingBlocks.Aspire.Hosting.SigNoz — agent notes

Local-dev Aspire AppHost integration for SigNoz. Install: `dotnet add package BuildingBlocks.Aspire.Hosting.SigNoz`. TFM: net10.0 (Aspire 13.4). Docker required.

Production telemetry belongs in `BuildingBlocks.Telemetry` against an OTLP backend — this package only provisions a local SigNoz stack.

## When to choose this

AppHost needs a local SigNoz UI + OTLP collector (ClickHouse, ZooKeeper, schema migrator). Not for production exporters.

## Register

```csharp
var signoz = builder.AddSigNoz("signoz")
    .WithUi()
    .WithDashboards();

builder.AddProject<Projects.Api>("api")
    .WithSigNozOtlpExporter(signoz);
```

- `WithSigNozOtlpExporter` is for `ProjectResource` only (`OTEL_EXPORTER_OTLP_*`, not `WithReference`).
- `WithDashboards()` seeds ASP.NET Core + BuildingBlocks dashboards (match on `spec.display.name`).
- Persist ClickHouse/ZooKeeper with `WithDataVolume()` / `WithDataBindMount()` when you need history across restarts.
- Default `Lifetime = Persistent` plus sqlite is not a wipe each run.

## Do not use for

Production OTLP, non-Aspire hosts, or treating this as a substitute for `BuildingBlocks.Telemetry`.
