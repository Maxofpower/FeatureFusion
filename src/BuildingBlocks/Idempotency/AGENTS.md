# BuildingBlocks.Idempotency — agent notes

**Status: COMPLETE — 1.0.1**

HTTP **Idempotency-Key** for ASP.NET Core. Consumer docs: [PACKAGE_README.md](PACKAGE_README.md). Lab guide: [docs/building-blocks/idempotency.md](../../../docs/building-blocks/idempotency.md).

Install: `dotnet add package BuildingBlocks.Idempotency`.

Lab FeatureFusion hosts this package — **do not** reintroduce Lab-local filter copies. Unit tests: `tests/BuildingBlocks/Idempotency.Tests`. Provenance Exp **3 / 4 / 12** (and lease Exp **15**).

## Register (Lab shape)

```csharp
builder.Services.AddBuildingBlocksIdempotency(o =>
{
    o.UserIdFallback = "123"; // Lab-only; not a package default
    o.ProcessingTtl = TimeSpan.FromMinutes(2);
})
.UseRedisLock()
.UseTelemetry();

// host OTel:
telemetry.AddSource("BuildingBlocks.Idempotency");

[Idempotent(useLock: true)]
public async Task<ActionResult> Create(...) { }

app.MapPost("/path", handler).WithIdempotency(useLock: true);
```

## Semantics (do not regress)

- Shared `IdempotencyGate` for MVC + Minimal API
- Cache all **2xx**; replay envelope + configurable replay header
- Errors: ProblemDetails (`https://buildingblocks.dev/errors/idempotency/...`)
- Fingerprint default **off** (Exp 3); on → method+path+body SHA-256 (Exp 12)
- Lock only around GetOrCreate when `UseLock` (Exp 4)
- MVC ObjectResult body and cache envelope: System.Text.Json; Minimal API `IResult`: System.Text.Json
- Telemetry optional; no cache-key tag by default; no BuildingBlocks.Telemetry package ref

## Lab evidence

| Exp | Role |
|-----|------|
| **3** CacheVsProduction | Miss → production, hit → replay; fingerprint-off body tolerance |
| **4** ConcurrencyRace | Redis SET NX + Processing/Completed under concurrent same-key clients |
| **12** Fingerprint | Opt-in mismatch → 422 vs same-body replay |
| **15** ProcessingLease | Expired ProcessingTtl can admit overlapping production |

Catalog: `tests/Lab/IntegrationTests/Experiments/README.md`.
