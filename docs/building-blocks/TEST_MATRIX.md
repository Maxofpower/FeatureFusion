# BuildingBlocks.Mediator v1.0 — test matrix

Release gate: scenarios below are covered by `tests/BuildingBlocks.Mediator.Tests`
on **`net8.0`**, **`net9.0`**, and **`net10.0`** (same suite per TFM).

## Supported

| Area | Scenarios |
|------|-----------|
| Send | Command / query / void; interface variables; `Send(object)`; nested Send |
| Pipeline | Registration order + explicit `order`; short-circuit; throw before/after; open + closed |
| Filters | `CommandPipelineBehavior` / `QueryPipelineBehavior` skip opposite kind |
| DI scan | Finds closed + open-generic handlers; Skip if pre-registered; ignores abstract/non-public |
| Telemetry | Success / fault / omit; Activity wraps full pipeline |
| ValidateOnStartup | Missing / duplicate / orphan / off; open-generic satisfies closed message |
| Handler lifetime | `HandlerLifetime` Transient/Scoped/Singleton on discovered handlers |
| Ambiguous handlers | Send throws when multiple closed (or multiple open) matches |
| Open-generic handlers | On-demand closing; closed registration preferred over open |
| Concurrency | Scoped parallel Sends; per-request pipeline order |
| Generics / Result | `Result<T>`, nested Result, void + Result |
| Validation (host pattern) | No validator skip; aggregate; DTO isolation |
| Cancellation | Explicit token from behavior (incl. `None`) reaches handler |

## Explicitly unsupported

| Non-goal | Expectation |
|----------|-------------|
| Publish / `INotification` | No public API |
| Streaming | No `CreateStream` / stream request types |
| Exception handlers (recover) | Faults rethrow |
| Pre/post processors | Use behaviors |
| Non-generic `IQuery` | Type does not exist |
| FV / metrics / Scrutor / other mediators in core | No package references |
| Open-generic `HandlerLifetime` | Always Transient via ActivatorUtilities |

## Edge

Null message → `ArgumentNullException`; unknown `Send(object)` → `ArgumentException`; missing handler → `InvalidOperationException` with type name; bad `AddOpenBehavior` → `ArgumentException`; AddMediator without assembly → `InvalidOperationException`.
