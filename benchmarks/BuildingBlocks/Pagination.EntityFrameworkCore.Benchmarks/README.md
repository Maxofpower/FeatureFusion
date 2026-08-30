# Pagination EF Core benchmarks

Fair comparison of **FeatureFusion** `ToCursorPageAsync`, EF Core `OrderBy`+`Skip`+`Take` (**OFFSET**), and **MR.EntityFrameworkCore.KeysetPagination 1.5.0** on the same file SQLite database, indexes, page size, and unique tie-breaker.

Do **not** quote `--job Dry` or cold-start numbers. Do **not** treat the old 5k in-memory job as the 10M result. Codec microseconds stay in `CursorCodecBenchmarks` and are never mixed with SQL milliseconds.

`--probe` is a **Stopwatch** harness (untimed ID verify, then 1 warmup + 5 repeats). It is not BenchmarkDotNet. It prints mean wall-clock **and** mean managed allocations per page (`GC.GetAllocatedBytesForCurrentThread`, labeled KB). BDN Default job is a separate 1M-row measurement (`MemoryDiagnoser`).

## Environment

| Item | Value |
|------|--------|
| CPU | 11th Gen Intel Core i9-11900K @ 3.50 GHz (8 cores / 16 logical) |
| RAM | 63.8 GB |
| OS | Windows 10.0.26200 |
| Runtime | .NET SDK 10.0.301, runtime 10.0.9 (`net10.0`) |
| EF Core | 10.0.0 |
| BenchmarkDotNet | 0.15.4, **Default** job + `MemoryDiagnoser` (1M-row job only) |
| Probe | Stopwatch `--probe` (1 warmup + 5 repeats; ms + managed KB / page) |
| Provider | SQLite (file, WAL; `synchronous=OFF` during insert only) |
| Page size | 20 |
| BDN dataset | 1,000,000 rows (`PAGINATION_BENCH_ROWS`) |
| Probe dataset | 10,000,000 or **100,000,000** (`--probe 10000000` / `--probe 100000000`) |
| Indexes | `(Price, Id)`, `(CreatedAt DESC, Id DESC)` |
| Competitor | MR.EntityFrameworkCore.KeysetPagination **1.5.0** |
| Native SQLite | `SQLitePCLRaw.bundle_e_sqlite3` **3.0.3** (not the vulnerable 2.1.11 transitive pin) |

SQLite plans are not SQL Server or PostgreSQL plans. FeatureFusion vs MR is query-shape (opaque cursor vs reference entity), not a claim that one API is universally faster.

## Reproduce

```bash
dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --filter *CursorCodec*
dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --filter *Keyset*
dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --probe 10000000
dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --probe 100000000
```

Optional:

- `PAGINATION_PROBE_DB` — persist the SQLite file (required for 100M; needs ~20+ GB free). Without it, 100M is written under `%TEMP%` only if that volume has space.
- `PAGINATION_BENCH_ROWS` — BDN row count (default 1M; use 100000 for a smoke run). Do not set BDN to 100M; use `--probe 100000000` for the large catalog.

Each BDN `GlobalSetup` and the probe **fail the process** if OFFSET, FeatureFusion, and MR return different ID sequences. The probe prints `Verified IDs skip=… (not timed)` (live OFFSET `Skip`+`Take` once) then `TimeAndAlloc()` without that OFFSET check.

## What is published

Package README quotes two labeled `--probe` tables (mean of 5 warmed repeats, file SQLite, index `(Price, Id)`, page 20, .NET 10; ms plus mean managed KB per page). Never publish Dry artifacts (`BenchmarkDotNet.Artifacts/` is gitignored).

**10M** (`--probe 10000000`):

| Skip | OFFSET | FeatureFusion | MR 1.5.0 |
|------|--------|---------------|----------|
| 0 | 0.5 ms / 77 KB | 0.6 ms / 79 KB | 0.5 ms / 75 KB |
| 1,000,000 | 29.7 ms / 77 KB | 15.5 ms / 85 KB | 18.2 ms / 86 KB |
| 5,000,000 | 154.9 ms / 77 KB | 17.8 ms / 85 KB | 19.9 ms / 86 KB |

**100M** (`--probe 100000000`, persist with `PAGINATION_PROBE_DB`):

| Skip | OFFSET | FeatureFusion | MR 1.5.0 |
|------|--------|---------------|----------|
| 0 | 0.6 ms / 77 KB | 0.7 ms / 79 KB | 0.6 ms / 75 KB |
| 10,000,000 | 737.9 ms / 75 KB | 379.0 ms / 84 KB | 427.0 ms / 84 KB |
| 50,000,000 | 2470.4 ms / 75 KB | 177.2 ms / 83 KB | 218.0 ms / 85 KB |

On this 100M catalog, skip 50M is about **14×** OFFSET (177 ms vs 2470 ms). Keyset grows more slowly than OFFSET; that is not a ranking of libraries on every provider.

## Limitations

- Hardware-dependent; rerun locally
- 100M catalog is the large-table probe; Default BDN stays at 1M so contributors can finish
- First / mid (`N/10`) / deep (`N/2`) pages; forward; backward last-page job
- Overhead job is `ToQueryString` only (library vs SQLite)
- SQL Server matrix is optional and not required to publish the SQLite story
