# Contributing

Thanks for contributing to **FeatureFusion** — a .NET lab of production **BuildingBlocks** packages plus a runnable showcase.

Formerly published as `Maxofpower/FeatureManagement`; GitHub redirects the old URL.

## Development

1. Clone [FeatureFusion](https://github.com/Maxofpower/FeatureFusion).
2. `dotnet restore FeatureFusion.sln`
3. Package tests (multi-TFM where applicable):
   - `dotnet test tests/BuildingBlocks/Mediator.Tests`
   - `dotnet test tests/BuildingBlocks/Mediator.Analyzers.Tests`
   - `dotnet test tests/BuildingBlocks/Telemetry.Tests`
   - `dotnet test tests/BuildingBlocks/Aspire.Hosting.SigNoz.Tests`
4. Pack: `dotnet pack src/BuildingBlocks/Mediator -c Release -o artifacts/nuget`

## Guidelines

### BuildingBlocks.Mediator

- Keep the public surface CQRS-first (`ICommand` / `IQuery`); no Publish in v1.
- Prefer `ICommandPipelineBehavior` / `IQueryPipelineBehavior` for new command/query-only open generics.
- Do not add Scrutor, FluentValidation, or other mediator/messaging packages as dependencies of the core library.
- Add/adjust tests for supported and explicitly unsupported behaviors ([TEST_MATRIX.md](docs/building-blocks/TEST_MATRIX.md)).
- Public API changes: update `PublicAPI.Unshipped.txt` / XML docs.

### BuildingBlocks.Telemetry / SigNoz hosting

- Keep OpenTelemetry packages on stable versions (no prerelease deps).
- Do not log endpoints, OTLP headers, or connection strings.

### Lab (FeatureFusion)

- Prefer vertical-slice feature folders under `Features/{Name}/`.
- Leave `Microsoft.FeatureManagement` and `FeatureManagement` JSON as the original feature-flag demo — not the product identity.

Contributions are accepted under the project **MIT** license (inbound = outbound).

## Pull requests

- Prefer focused PRs with tests.
- Run the relevant test project(s) green before requesting review.
- When you publish a LinkedIn post tied to this repo, add a row in [docs/linkedin-posts.md](docs/linkedin-posts.md).
