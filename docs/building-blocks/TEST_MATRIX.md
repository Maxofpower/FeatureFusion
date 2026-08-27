# BuildingBlocks.Mediator v1.1 — test matrix

Release gate: scenarios below are covered by `tests/BuildingBlocks/Mediator.Tests`
on **`net8.0`**, **`net9.0`**, and **`net10.0`** (same suite per TFM).

## Supported

| Area | Scenarios |
|------|-----------|
| Send | Command / query / void; interface variables; `Send(object)`; nested Send |
| Pipeline | Registration order + explicit `order`; short-circuit; throw before/after; open + closed |
| Filters (1.0) | `CommandPipelineBehavior` / `QueryPipelineBehavior` skip opposite kind |
| Typed behaviors (1.1) | `ICommandPipelineBehavior` not constructed for queries; `IQueryPipelineBehavior` not constructed for commands; both in one pipeline isolate by kind; void commands; mixed with unconstrained; open-generic messages (`EchoCommand<T>` / `EchoQuery<T>`); `Result<T>` / nested `Result<PagedResult<T>>` / `IReadOnlyList<T>`; wrong kind / unconstrained / 1.0 filter base / closed type / null rejected with `ArgumentException`/`ArgumentNullException` and the matching interface name |
| DI scan | Finds closed + open-generic handlers; Skip if pre-registered; ignores abstract/non-public |
| Telemetry | Success / fault / omit; Activity wraps full pipeline; Meter duration + send counter; `EnableMetrics = false`; custom meter name |
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
| FV / Scrutor / other mediators in core | No package references (Meter is BCL `System.Diagnostics.Metrics`) |
| Open-generic `HandlerLifetime` | Always Transient via ActivatorUtilities |

## Edge

Null message → `ArgumentNullException`; unknown `Send(object)` → `ArgumentException`; missing handler → `InvalidOperationException` with type name; bad `AddOpenBehavior` / `AddOpenCommandBehavior` / `AddOpenQueryBehavior` → `ArgumentException`; AddMediator without assembly → `InvalidOperationException`.
