# BuildingBlocks.Pagination

Typed keyset (cursor) pagination. **One NuGet:** `BuildingBlocks.Pagination.EntityFrameworkCore` (IR + EF Core adapter). Dapper is an in-repo project, not a package.

See ADR [0003](../adr/0003-pagination-keyset.md), the [test matrix](PAGINATION_TEST_MATRIX.md), and [`PACKAGE_README.md`](../../src/BuildingBlocks/Pagination.EntityFrameworkCore/PACKAGE_README.md).

## Install

```bash
dotnet add package BuildingBlocks.Pagination.EntityFrameworkCore
```

Requires .NET 8 / 9 / 10. The nupkg includes `BuildingBlocks.Pagination.dll` (`SortKey`, cursors — the shared **intermediate representation** that adapters execute). Core and Dapper projects in this repo are **not** packed. PackageRef `Microsoft.EntityFrameworkCore` matches the TFM (8 / 9 / 10).

## SortKey

```csharp
var key = SortKey.For<Product>()
    .By(p => p.Price)
    .ThenByUnique(p => p.Id);
```

Map a host enum with `SortKeyRegistry<TEnum, TEntity>` — do not parse `"Price"` into a property. An unregistered enum throws `InvalidOperationException` (not `InvalidCursor`).

**Sort the stored scalar.** Enums encode as their numeric underlying value. Value objects: `p => p.Money.Amount`, not `p => p.Money`. Nullable value types (`int?`, `DateTime?`, nullable enums) are rejected (`NullableSortUnsupported`); coalesce in the model. `NullOrder` does not emit `NULLS FIRST/LAST`.

## EF Core

```csharp
var page = await db.Products
    .AsNoTracking()
    .TagWith("products.list")
    .ToCursorPageAsync(new CursorRequest(cursor, 20), key);
```

Source layout matches EF Core packages: `Extensions/` (public API), `Query/Internal/` (OrderBy + seek), `Infrastructure/Internal/` (DbContext / shadow).

Host `OrderBy` is **replaced** by the sort key, not merged. There is no `IEnumerable` overload.

Optional `PaginationOptions.Hint` defaults to `None` (no extra SQL or transaction). `QueryHint.ReadUncommitted` is SQL Server **session isolation** (`READ UNCOMMITTED`), not table-hint `WITH (NOLOCK)`. EF begins one `ReadUncommitted` transaction around COUNT (if requested) and PAGE when there is no ambient transaction, then restores `READ COMMITTED` on the still-open connection; an ambient transaction is ignored (no nest). PostgreSQL and Sqlite no-op. Dapper uses a `SET TRANSACTION ISOLATION LEVEL` prefix, then restores `READ COMMITTED` on the open connection. Host `AsNoTracking` / `TagWith` / Dapper `WITH (NOLOCK)` still work when `Hint` is omitted. Prefer the `Select` overload so DTOs are projected in SQL. `HasKeysetIndex(sortKey)` creates the matching composite index (add a DESC variant when the first column is descending).

Set `SigningKey` on public HTTP APIs so clients cannot forge `Walk` or key values.

**SQL vs in-memory:** one adapter. Relational providers execute seek SQL. EF InMemory is for tests. FeatureFusion uses PostgreSQL; the published numbers below are SQLite SQL with `(Price, Id)`. Guid CLR order matches SQLite/PostgreSQL, not SQL Server `uniqueidentifier`.

## Dapper (repo project only)

FeatureFusion `POST /api/v2/Product/products-dapper` (same catalog as `GET /api/v2/products-page`). Not published to NuGet. Host SQL must not contain `ORDER BY` / `OFFSET` / `LIMIT`.

## Cursors

`v1.{payload}` or `v1.{payload}.{hmac}`. Empty/whitespace cursor = first page. Empty cursor + `PageDirection.Backward` = last page. Slot JSON that cannot be decoded is `PaginationException(InvalidCursor)`. Hosts map `PaginationException.Code` to HTTP 400 — do not map registry misses that way.

## Runnable showcase (FeatureFusion)

FeatureFusion is the integration lab: a PostgreSQL `products` catalog (~1000 seeded rows) using **one** `GetProductsQuery` / `ProductService` on MVC, Minimal API, Dapper, and MCP.

Primary HTTP surface:

```http
GET /api/v2/products-page?limit=20&sortBy=Price&sortDirection=Ascending
```

First page returns `items`, `hasMore`, `nextCursor`, `previousCursor`, `hasPrevious`, and `totalCount`. Cursors are **opaque** — pass `nextCursor` or `previousCursor` back unchanged.

```http
GET /api/v2/products-page?limit=20&sortBy=Price&sortDirection=Ascending&cursor=<NextCursor>
GET /api/v2/products-page?limit=20&sortBy=Price&sortDirection=Ascending&cursor=<PreviousCursor>
```

Empty cursor + `pageDirection=Backward` starts at the last page (`GET /api/v2/products-page?limit=20&pageDirection=Backward`). `sortBy`: `Id` · `Name` · `Price` · `CreatedAt` (each composite key ends with unique `Id`). `sortDirection`: `Ascending` · `Descending`.

Same query on:

- `POST /api/v2/products-page` — Minimal API (compatibility)
- `POST /api/v2/Product/products` — MVC EF (`AsNoTracking`, `TagWith("products.list")`, SQL `Select` to `ProductDto`)
- `POST /api/v2/Product/products-dapper` — Dapper adapter (in-repo, not packed)
- MCP `products.list`

`HasKeysetIndex` on the Price, Name, and CreatedAt keys (ASC and DESC). Id-only sorts use the primary key. PostgreSQL — **`QueryHint` stays `None`**. Do not set `QueryHint.ReadUncommitted` in this lab (that option is SQL Server session isolation, not a FeatureFusion demo).

## Benchmarks

Indexed keyset on SQLite **SQL** (file, index on `(Price, Id)`, page 20). `--probe` is Stopwatch (1 warmup + 5 repeats), not BenchmarkDotNet, not EF InMemory, not `--job Dry`. 10M rows, skip 5M: keyset **17.8 ms**, `OFFSET` **154.9 ms**, MR 1.5.0 **19.9 ms**. 100M catalog, skip 50M: keyset **177.2 ms**, `OFFSET` **2470.4 ms**, MR **218.0 ms**. OFFSET IDs are verified once (untimed) before timing. First page is cheap for both.

```bash
dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --filter *CursorCodec*
dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --filter *Keyset*
dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --probe 10000000
dotnet run -c Release --project benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks -- --probe 100000000
```

See the [package README](../../src/BuildingBlocks/Pagination.EntityFrameworkCore/PACKAGE_README.md) and [benchmarks README](../../benchmarks/BuildingBlocks/Pagination.EntityFrameworkCore.Benchmarks/README.md).
