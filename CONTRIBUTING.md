# Contributing

Thanks for contributing to **BuildingBlocks.Mediator**.

## Development

1. Clone [FeatureManagement](https://github.com/Maxofpower/FeatureManagement).
2. `dotnet test tests/BuildingBlocks.Mediator.Tests`
3. `dotnet pack src/BuildingBlocks.Mediator -c Release -o artifacts/nuget`

## Guidelines

- Keep the public surface CQRS-first (`ICommand` / `IQuery`); no Publish in v1.
- Do not add Scrutor, FluentValidation, or other mediator/messaging packages as dependencies of the core library.
- Add/adjust tests for supported and explicitly unsupported behaviors ([TEST_MATRIX.md](docs/building-blocks/TEST_MATRIX.md)).
- Public API changes: update `PublicAPI.Unshipped.txt` / XML docs.
- Contributions are accepted under the project **MIT** license (inbound = outbound).

## Pull requests

- Prefer focused PRs with tests.
- Run the Mediator test project green before requesting review.
