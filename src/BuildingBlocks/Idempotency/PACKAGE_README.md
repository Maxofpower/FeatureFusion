# BuildingBlocks.Idempotency

ASP.NET Core **Idempotency-Key** for MVC and Minimal API. Host-owned `IDistributedCache`, optional Redis lock, opt-in request fingerprint, RFC 9457 ProblemDetails on conflicts, optional ActivitySource.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Idempotency.svg)](https://www.nuget.org/packages/BuildingBlocks.Idempotency)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Requires **.NET 8**, **.NET 9**, or **.NET 10**.

## Install

```bash
dotnet add package BuildingBlocks.Idempotency
```

## Features (1.0.0)

- Shared gate for **MVC** (`[Idempotent]`) and **Minimal API** (`WithIdempotency`)
- Cache **HTTP 2xx** envelopes (status, content-type, body) and replay them
- Processing lease (`ProcessingTtl`) with abandoned recovery; Completed window (`EntryTtl`)
- Per-endpoint TTL overrides (`ProcessingTtlSeconds` / `EntryTtlSeconds`)
- Optional Redis **SET NX** lock around GetOrCreate (`.UseRedisLock()`)
- Opt-in fingerprint: `SHA-256(method + "\n" + path + "\n" + body)`
- Key validation (ULID by default, `MaxKeyLength`, reject empty / control characters)
- `DuplicateCompletedBehavior`: Replay (default) or Conflict
- ProblemDetails errors with stable `type` URIs
- Optional telemetry (`.UseTelemetry()`); host `AddSource("BuildingBlocks.Idempotency")`

## Quick start

```csharp
// Host must already register IDistributedCache.
// For UseLock, also register IConnectionMultiplexer.
builder.Services.AddBuildingBlocksIdempotency(o =>
{
    o.ProcessingTtl = TimeSpan.FromMinutes(2); // keep longer than worst-case handler
    // o.EnableRequestFingerprint = true;
    // o.UserIdFallback = "anonymous"; // when NameIdentifier is missing
})
.UseRedisLock()      // optional
.UseTelemetry();     // optional

// MVC
[HttpPost]
[Idempotent(useLock: true)]
public async Task<ActionResult<OrderResponse>> Create([FromBody] CreateOrder request) { ... }

// Minimal API
app.MapPost("/orders", CreateAsync).WithIdempotency(useLock: true);
```

Aliases still work: `AddRedisIdempotencyLock()`, `AddIdempotencyTelemetry()`.

## Behavior

| Situation | Result |
|-----------|--------|
| New key | Execute; on **2xx** store Completed envelope |
| Same key, Completed (default) | Replay envelope + `X-Idempotent-Response` |
| Same key, Completed + `DuplicateCompletedBehavior.Conflict` | ProblemDetails (default 409) |
| Same key, Processing (lease active) | ProblemDetails (default 409) |
| Same key, Processing expired | Miss — may execute again |
| Fingerprint enabled + method/path/body mismatch | ProblemDetails (default 422) |
| Missing / invalid key | ProblemDetails 400 |
| Missing caller identity (no `UserIdFallback`) | ProblemDetails 401 |
| Non-2xx or exception | Remove cache entry (retry allowed) |
| `UseLock` and lock not acquired | ProblemDetails 500 |

Cache key: `{KeyPrefix}_{optionalScopeClaims}_{userId}_{key}` (default prefix `Idempotency`).

Replay header name is configurable (`CachedResponseHeader`, default `X-Idempotent-Response`).

### ProblemDetails

Conflicts and validation failures return `application/problem+json`. Stable `type` values:

| Suffix | Typical status |
|--------|----------------|
| `key-invalid` | 400 |
| `unauthorized` | 401 |
| `processing` | 409 |
| `fingerprint-mismatch` | 422 |
| `duplicate` | 409 |
| `lock-failure` | 500 |

Base: `https://buildingblocks.dev/errors/idempotency/`.

### TTL

| Setting | Default | Role |
|---------|---------|------|
| `ProcessingTtl` | 2 min | In-flight lease |
| `EntryTtl` | 30 min | Completed replay window |
| Attribute / `WithIdempotency` seconds | `0` | Use global; `> 0` overrides |

After `ProcessingTtl` expires, another request with the same key may run while a slow first request is still in flight. The optional lock covers GetOrCreate only.

### Fingerprint

Off by default (same key replays even if the body changes). When `EnableRequestFingerprint` is true, the hash includes HTTP method, path, and raw body bytes (not canonical JSON).

## Options (common)

| Option | Default | Notes |
|--------|---------|-------|
| `HeaderName` | `Idempotency-Key` | Request header |
| `RequireUlid` | `true` | Set `false` for opaque strings (still length/control checks) |
| `MaxKeyLength` | `256` | Always enforced |
| `KeyScopeClaimTypes` | empty | Extra claims in the cache key (e.g. tenant) |
| `UserIdFallback` | `null` | Missing user claim → 401 unless set |
| `DuplicateCompletedBehavior` | `Replay` | Or `Conflict` |
| `FingerprintConflictStatusCode` | `422` | Configurable |
| `ProcessingConflictStatusCode` | `409` | Configurable |
| `DuplicateConflictStatusCode` | `409` | When Conflict strategy |

## Telemetry

`.UseTelemetry()` registers ActivitySource `BuildingBlocks.Idempotency`. Outcomes: `executed`, `replayed`, `processing_conflict`, `fingerprint_conflict`, `duplicate_conflict`, `bad_key`, `unauthorized`, `lock_failure`. Cache keys are not tagged unless `IncludeCacheKeyInTelemetry` is true.

This package does **not** depend on BuildingBlocks.Telemetry — add `AddSource("BuildingBlocks.Idempotency")` on the host.

## Host requirements

- `IDistributedCache` (required)
- `IConnectionMultiplexer` when using `.UseRedisLock()` / `UseLock: true`
- No references to BuildingBlocks.Mcp, Mediator, Telemetry, or EventBus

## Not this package

MCP tool write idempotency (`UseMemoryIdempotency` / `IMcpIdempotencyStore`) is part of **BuildingBlocks.Mcp**.
