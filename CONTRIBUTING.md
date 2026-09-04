# Contributing

Thanks for contributing to **FeatureFusion** — a .NET lab of production **BuildingBlocks** packages plus a runnable showcase.

Formerly published as `Maxofpower/FeatureManagement`; GitHub redirects the old URL.

## Development

1. Clone [FeatureFusion](https://github.com/Maxofpower/FeatureFusion).
2. `dotnet restore FeatureFusion.sln`
3. Package tests (multi-TFM where applicable):
   - `dotnet test tests/BuildingBlocks/Mediator.Tests`
   - `dotnet test tests/BuildingBlocks/Mediator.Analyzers.Tests`
   - `dotnet test tests/BuildingBlocks/Mcp.Tests`
   - `dotnet test tests/BuildingBlocks/Idempotency.Tests`
   - `dotnet test tests/BuildingBlocks/Pagination.Tests`
   - `dotnet test tests/BuildingBlocks/Pagination.EntityFrameworkCore.Tests`
   - `dotnet test tests/BuildingBlocks/Pagination.Dapper.Tests`
   - `dotnet test tests/BuildingBlocks/Telemetry.Tests`
   - `dotnet test tests/BuildingBlocks/Aspire.Hosting.SigNoz.Tests`
4. Pack: `dotnet pack src/BuildingBlocks/Mediator -c Release -o artifacts/nuget` (same for Telemetry, Mcp, Idempotency, `Pagination.EntityFrameworkCore`)

Release tags (must match the project `<Version>`): `mediator-v*`, `mcp-v*`, `idempotency-v*`, `telemetry-v*`, `pagination-v*`, `telemetry-signoz-v*` (see `.github/workflows/*-release.yml`).

## Guidelines

### BuildingBlocks.Mediator

- Keep the public surface CQRS-first (`ICommand` / `IQuery`); no Publish in v1.
- Prefer `ICommandPipelineBehavior` / `IQueryPipelineBehavior` for new command/query-only open generics.
- Do not add Scrutor, FluentValidation, or other mediator/messaging packages as dependencies of the core library.
- Add/adjust tests for supported and explicitly unsupported behaviors ([TEST_MATRIX.md](docs/building-blocks/TEST_MATRIX.md)).
- Public API changes: update `PublicAPI.Unshipped.txt` / XML docs.

### BuildingBlocks.Pagination.EntityFrameworkCore

- **One** packable project: `Pagination.EntityFrameworkCore`. Core IR and Dapper are `IsPackable=false`.
- Keep EF layout: `Extensions/` (public API), `Query/Internal/`, `Infrastructure/Internal/`.
- Npgsql NULLS: `AddBuildingBlocksPagination` + `UseBuildingBlocksPagination` (tagged command interceptor `BuildingBlocks.Pagination:First|Last`, not `AsyncLocal`). Do not CASE-wrap `OrderBy`. `HasKeysetIndex(sortKey, NullOrder)` is optional Npgsql index metadata; the one-argument overload does not write it.
- Core stays dependency-free of ORMs. Map host enums to prebuilt `SortKey`s; do not parse property-name strings.
- Add/adjust tests for supported and unsupported rows ([PAGINATION_TEST_MATRIX.md](docs/building-blocks/PAGINATION_TEST_MATRIX.md)).
- Public API changes: update `PublicAPI.Shipped.txt` / XML docs.
- Benchmarks: `dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --filter "*" --job Dry` to smoke; drop `--job Dry` for measurement.

### BuildingBlocks.Idempotency

- HTTP `Idempotency-Key` only — do not fold MCP `IMcpIdempotencyStore` into this package.
- Host owns `IDistributedCache` (and `IConnectionMultiplexer` when using the Redis lock).
- Prefer **System.Text.Json** for cache / MVC body capture (no Newtonsoft.Json).
- Lab provenance: Experiments 3 / 4 / 12 under `tests/Lab/IntegrationTests/Experiments/`.
- Release tag: `idempotency-v*` must match `<Version>` (current **1.0.1**).

### BuildingBlocks.Telemetry / SigNoz hosting

- Keep OpenTelemetry packages on stable versions (no prerelease deps).
- Do not log endpoints, OTLP headers, or connection strings.

### Lab (FeatureFusion)

- Prefer vertical-slice feature folders under `Features/{Name}/`.
- Leave `Microsoft.FeatureManagement` and `FeatureManagement` JSON as the original feature-flag demo — not the product identity.
- Lab charter and evidence families: [docs/lab/README.md](docs/lab/README.md). Do not invent a Scenario/ExperimentEngine NuGet without crossing the extraction rule there.
- Integration experiments: [Experiments README](tests/Lab/IntegrationTests/Experiments/README.md). Prefer observation helpers over production changes.

Contributions are accepted under the project **MIT** license (inbound = outbound).

## Pull requests

- Prefer focused PRs with tests.
- Run the relevant test project(s) green before requesting review.
- New packable BuildingBlock: add `AGENTS.md` + `PACKAGE_README.md`, a row in [docs/linkedin-posts.md](docs/linkedin-posts.md), and an entry in the root [AGENTS.md](AGENTS.md) / [llms.txt](llms.txt). Do not put LinkedIn URLs in `PACKAGE_README.md` or packed XML docs.
- When I publish a LinkedIn or Medium post tied to this repo, I'll add a row in [docs/linkedin-posts.md](docs/linkedin-posts.md) and a line in the root README LinkedIn catalog.
