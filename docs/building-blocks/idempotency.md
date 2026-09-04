# BuildingBlocks.Idempotency

ASP.NET Core HTTP **Idempotency-Key** for MVC and Minimal API: host-owned `IDistributedCache`, optional Redis lock, opt-in fingerprint, ProblemDetails on conflicts, optional ActivitySource.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Idempotency.svg)](https://www.nuget.org/packages/BuildingBlocks.Idempotency)
[![GitHub Release](https://img.shields.io/github/v/release/Maxofpower/FeatureFusion?filter=idempotency-v*&logo=github&label=GitHub%20Release)](https://github.com/Maxofpower/FeatureFusion/releases?q=idempotency-v)

**Current package version: 1.0.1.** Canonical consumer docs: [package README](../../src/BuildingBlocks/Idempotency/PACKAGE_README.md). Agent notes: [AGENTS.md](../../src/BuildingBlocks/Idempotency/AGENTS.md).

## What's new in 1.0.1

- NuGet package icon
- **System.Text.Json** for cache envelope and MVC `ObjectResult` capture (no Newtonsoft.Json). No public API change from 1.0.0.

## Install

```bash
dotnet add package BuildingBlocks.Idempotency
```

Targets **.NET 8 / 9 / 10**. Register `IDistributedCache` on the host. For locked endpoints, register `IConnectionMultiplexer` and call `.UseRedisLock()`.

## Register

```csharp
builder.Services.AddBuildingBlocksIdempotency(o =>
{
    o.ProcessingTtl = TimeSpan.FromMinutes(2); // longer than worst-case handler
    // o.EnableRequestFingerprint = true;
    // o.UserIdFallback = "anonymous";
})
.UseRedisLock()
.UseTelemetry(); // optional — also AddSource("BuildingBlocks.Idempotency")
```

```csharp
// MVC
[HttpPost]
[Idempotent(useLock: true)]
public async Task<ActionResult<OrderResponse>> Create([FromBody] CreateOrder request) { ... }

// Minimal API
app.MapPost("/orders", CreateAsync).WithIdempotency(useLock: true);
```

## Behavior (summary)

| Situation | Result |
|-----------|--------|
| New key | Execute; cache **2xx** envelope |
| Completed (default) | Replay + `X-Idempotent-Response` |
| Processing (active lease) | ProblemDetails 409 |
| Fingerprint mismatch | ProblemDetails 422 |
| Bad key / no identity / lock fail | ProblemDetails 400 / 401 / 500 |
| Non-2xx or exception | Drop cache entry |

Full tables (TTL, options, ProblemDetails `type` URIs): [PACKAGE_README](../../src/BuildingBlocks/Idempotency/PACKAGE_README.md).

## FeatureFusion lab

| Surface | Path |
|---------|------|
| MVC (locked) | `POST /api/v2/Order/order` — `[Idempotent(useLock: true)]` |
| Minimal API smoke | `POST /api/v2/idempotency-smoke` — `.WithIdempotency(useLock: true)` |
| DI | `AddBuildingBlocksIdempotency` → `.UseRedisLock().UseTelemetry()`; OTel `AddSource("BuildingBlocks.Idempotency")` |

Behavioral provenance (regression gates): Experiments **3** (cache vs production), **4** (concurrency / lock), **12** (fingerprint) — [Experiments README](../../tests/Lab/IntegrationTests/Experiments/README.md). Exp **15** documents ProcessingTtl lease overlap.

Do not reintroduce Lab-local idempotency filter copies; use the package.

## Not this package

MCP write idempotency (`UseMemoryIdempotency`) lives in **BuildingBlocks.Mcp**.
