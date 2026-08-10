# Concepts

| Type | Role |
|------|------|
| `ISender` | Send API — prefer at call sites |
| `IMediator` | Extends `ISender` (no Publish in v1) |
| `ICommand` | Void write (`ICommand : ICommand<Unit>`) |
| `ICommand<T>` | Write with response |
| `IQuery<T>` | Read with response (no non-generic `IQuery`) |
| `Unit` | Void pipeline response |
| `ICommandHandler<>` / `<,>` / `IQueryHandler<,>` | Single handler per message |
| `IPipelineBehavior<,>` | Cross-cutting around handler |

## Lifetime

Default `ISender` / `IMediator` lifetime is **Scoped**. Prefer Scoped so handlers/behaviors that use scoped services are not captured. Setting `Lifetime = Singleton` risks captive dependencies.

Discovered handlers use `HandlerLifetime` (default **Transient**). Prefer Transient or Scoped; Singleton handlers must be thread-safe and must not depend on scoped services. Open-generic handlers always resolve as Transient regardless of `HandlerLifetime`.

At Send time the mediator requires **exactly one** matching handler (closed or open-generic). Zero or multiple registrations throw `InvalidOperationException` with the message type name.

## Registration

- `RegisterServicesFromAssembly` / `RegisterServicesFromAssemblyContaining<T>` — built-in scanner (no Scrutor).
- Handlers: public/nested-public concrete types (including open-generic definitions), Skip if already registered.
- Open-generic handlers are closed on demand when resolving a matching closed interface.
- `ValidateOnStartup` — exactly one handler per public/nested-public closed message in scanned assemblies (open-generic matches count).
