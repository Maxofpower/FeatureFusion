# ADR 0003 — Keyset pagination IR with an EF Core package

- **Status:** Accepted
- **Date:** 2026-08-29
- **Deciders:** Mohammad Hasan Hosseini
- **Related:** [docs/building-blocks/pagination.md](../building-blocks/pagination.md)

## Decision

1. **One NuGet** for v1: `BuildingBlocks.Pagination.EntityFrameworkCore`. The **intermediate representation** (IR: `SortKey`, cursors, seek slots) is a non-packable sibling project whose DLL is **bundled** in that nupkg. Consumers do not install a second Pagination package.
2. **Dapper** stays an **in-repo project**, not a package, until we choose to ship it. A LinqToDB adapter is **not** in this repository; a future adapter could reuse the same IR. Same rule as keeping MCP off Mediator: adapters do not leak into the shipped EF package as extra nupkgs.
3. Shared **seek IR** in the core project: ordered slots `{ Direction, ClrType, Value, SqlIdentifier?, Expression }`. The EF package executes; core does not reference EF.
4. EF project layout follows **EF Core package structure**: `Extensions/` (public `ToCursorPageAsync`), `Query/Internal/` (OrderBy + seek trees), `Infrastructure/Internal/` (DbContext / shadow). Mapped members use expressions; shadow uses typed `ByShadow<TValue>` → `EF.Property<TValue>`.
5. Cursors are opaque and versioned. No `pageIndex`. HMAC is optional in the API and **required for untrusted HTTP** (`SigningKey`). Slot decode failures are `InvalidCursor`. Unregistered `SortKeyRegistry.Get` is `InvalidOperationException`, not a bad cursor.
6. FeatureFusion is the showcase (Minimal API `GET /api/v2/products-page`, MVC `POST /api/v2/Product/products`, Dapper `products-dapper`, MCP `products.list` — one `GetProductsQuery`). No extra sample apps. Benchmarks: `benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks` (file SQLite: FeatureFusion vs OFFSET vs MR.EntityFrameworkCore.KeysetPagination; cursor codec separate). Publish Default-job or `--probe` only — never Dry.
7. **Query hints are optional and allowlisted.** `PaginationOptions.Hint` defaults to `QueryHint.None` (zero extra SQL). `QueryHint.ReadUncommitted` is SQL Server session isolation (`READ UNCOMMITTED`), not table-hint `WITH (NOLOCK)`. EF begins one transaction around COUNT (if requested) and PAGE when there is no ambient transaction, then restores `READ COMMITTED` on the still-open connection; an ambient transaction is ignored (no nest). Dapper prefixes `SET TRANSACTION ISOLATION LEVEL` and restores `READ COMMITTED`. PostgreSQL/Sqlite ignore it. No raw SQL strings (injection). Host `AsNoTracking` / `TagWith` / Dapper `WITH (NOLOCK)` remain valid when `Hint` is omitted. Kitchen-sink lock/index hints stay host-owned.
8. **No `IEnumerable` adapter.** Host `OrderBy` is replaced by `CursorOrder`, not merged. Nullable value-type sort slots are rejected (`NullableSortUnsupported`) because `NullOrder` does not emit `NULLS FIRST/LAST`.

## Consequences

Hosts `dotnet add package BuildingBlocks.Pagination.EntityFrameworkCore` and get `SortKey` + `ToCursorPageAsync`. Dapper remains a project reference in the lab, not a nupkg. A LinqToDB adapter is not in this repo; a future adapter could reuse the same IR without adding another nupkg to the EF package.
