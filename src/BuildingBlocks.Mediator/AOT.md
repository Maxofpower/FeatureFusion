# Native AOT / trimming notes

`BuildingBlocks.Mediator` uses runtime `Type.MakeGenericType` and `Activator.CreateInstance` for
handler and pipeline wrappers (`Mediator`, `RequestHandlerWrappers`, `PipelineBehaviorWrappers`).

## Guidance

- **net10.0 JIT / server apps:** fully supported (primary target).
- **Native AOT:** not fully supported in v1. Trimming may remove closed handler/behavior constructions
  that are only referenced via reflection.
- If you need AOT later: prefer source-generated registration (future) or explicit closed registrations
  plus trimmer roots for every closed `ICommandHandler` / `IQueryHandler` / `IPipelineBehavior` pair.

Do not set `<IsAotCompatible>true</IsAotCompatible>` on this package until wrappers are AOT-safe.
