# BuildingBlocks.Pagination v1 — test matrix

xUnit on **net8.0 / net9.0 / net10.0**. No coverlet gate. Fast CI: `.github/workflows/pagination.yml` (IR, EF Sqlite, Dapper SQL-gen). SQL Server QueryHint execute: `.github/workflows/pagination-queryhint-sqlserver.yml`. Release pack: `.github/workflows/pagination-release.yml` (tag `pagination-v*` must match the EF `<Version>`). **One nupkg:** `BuildingBlocks.Pagination.EntityFrameworkCore`. Dapper tests still run; that project is not packed.

Shared seed: `CatalogSeed` (12 rows, duplicate prices) is used by **EF Sqlite** and **Dapper Sqlite** so first/next/prev **id sequences match** for the same `SortKey`.

Each supported row has a matching test name prefix.

## Supported

| Prefix / area | Scenarios |
|---------------|-----------|
| `Enum_` | `ItemSortField` × `SortDirection` first page (ASC/DESC); exhaustive `SortKeyRegistry` (`Enum.GetValues`); next page no overlap; previous cursor restores first page; Price cursor + Name key → `CursorSortMismatch`; unknown enum is a registry miss (not string reflection) |
| `Expression_` / `String_Seek_` / `Guid_Seek_` / `Shadow_` / `No_Public_OrderBy` | Mapped `By(p => p.Price)` query has **no** `EF.Property`; string/Guid seek has **no** `CompareTo` / `string.Compare` and uses `>`; shadow `ByShadow<TValue>` **does** use `EF.Property`; unknown shadow fails before execute; nested `Vendor.Name` (EF); no `OrderBy(string)` API |
| `HasKeysetIndex_` | Composite index property order + DESC flags match `SortKey`; nested path → `UnsupportedKeysetIndex` |
| `Forward_` / `Previous_` / `Duplicate_` / `Empty_` / `Single_Page` / `Backward_` / `Replay_` | Empty table; single page `HasNext` false; `limit+1` HasNext; forward no overlap (**same ids as seed/EF**); backward restores first page; **empty cursor + `PageDirection.Backward` = last page**; **same next cursor replayed is stable**; composite Price+Id with duplicate prices; whitespace cursor = first page |
| `Postgres_` / `Mixed_Direction_` / `SqlServer_` / `Sqlite_` | Dapper: Postgres tuple SQL `(price, id) >` when directions match; mixed ASC/DESC is OR not tuple; SqlServer `FETCH NEXT`; Sqlite `LIMIT`. EF: OR-chain seek (provider-agnostic) + Sqlite `LIMIT` in `ToQueryString` |
| `Missing_Sql_` / `Shadow_Throws` / `Host_Where_` / `InvalidHostSql_` | Dapper missing `sql:` throws; Dapper rejects `ByShadow`; host WHERE params + `@ks0` no collision; host SQL with `ORDER BY` / `OFFSET` / `FETCH` / `LIMIT` → `InvalidHostSql`; `WITH (NOLOCK)` in host SQL is preserved inside the subquery |
| `IncludeTotalCount_` / `Limit_` / `Invalid_` / `Hmac_` / `Codec_` | Count on/off; limit 0 / over max; invalid/truncated/tampered cursor; HMAC reject unsigned when key set; concurrent encode/decode; `PaginationOptions.Default` is not a shared mutable singleton; default `Hint` is `None` |
| `Clr_` | `int`, `long`, `short`, `byte`, `float`, `double`, `bool`, `Guid`, `string`, `decimal` (EF InMemory + Dapper Sqlite), `DateTime` UTC, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, entity enum. **`T?` sort slots → `NullableSortUnsupported`** |
| `ValueObject_` / `UnsupportedSortType_` | Nested scalar `p => p.Money.Amount` and `p => p.TypedId.Value` succeed; whole complex type / `byte[]` / value-object wrapper / navigation → `UnsupportedSortType` |
| `Projection_` / `InMemory_Map` | EF `Select` DTO including key columns; DTO omitting a key fails; in-memory `ToCursorPageMappedAsync` |
| `Cancellation` | `CancellationToken` on EF `ToListAsync` and Dapper `CommandDefinition` |
| `Hmac_Signed_` / `Insert_And_Delete_` / `Mixed_Direction_` / `Registry_` | HMAC signed first/next/prev execute; insert+delete between pages keep unique keys stable; mixed ASC/DESC EF seek SQL is an OR-chain; unregistered `SortKeyRegistry.Get` is `InvalidOperationException` (not `InvalidCursor`); `FromCursorValue` bad JSON → `InvalidCursor` |
| `SortColumn_` | EF Sqlite execute: raising a later `Price`/`CreatedAt` before the cursor vanishes from page 2; lowering a far-ahead row can appear later. Inherent keyset; no uniqueness beyond the unique slot |
| `Nullable_String_` | EF Sqlite execute: `string?` null / `""` / names; `NullOrder.Last` and `First`; forward and backward. Seek-only (no `NULLS FIRST/LAST`); first-page `ORDER BY` follows Sqlite (NULL first) even when `Nulls = Last` |
| `Cancellation_After_` | EF interceptor cancels after `ReaderExecuting` (command started). Existing `Cancellation()` is pre-cancelled |
| `QueryHint_` | Default `None` emits no SET/NOLOCK; Dapper `ReadUncommitted` prefixes `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;` on SqlServer only (Postgres/Sqlite SQL unchanged), including COUNT-shaped SQL via `QueryHintSql.Apply`; restore `READ COMMITTED` is a separate execute (not in `BuildSql`); host `WITH (NOLOCK)` still inside the subquery; EF/Dapper Sqlite execute with `ReadUncommitted` is a no-op; **SQL Server execute** (Aspire, separate project): dirty read, COUNT+PAGE same isolation, commit, exception rollback, cancellation cleanup, ambient txn ignored; **same open connection** isolation is 1 during RU paging and 2 after success/exception/cancellation (pooling on); Dapper execute restore on open SqlConnection; CI (`GITHUB_ACTIONS` / `PAGINATION_SQLSERVER_REQUIRED`) never skips; IntegrationTests Postgres execute is a no-op |
| Lab HTTP | `FeatureFusionApiTests` — `GET /api/v2/products-page` first/next/prev; empty cursor + `pageDirection=Backward` last page; GET matches POST; every `ProductSortField` × direction; JSON `"SortBy":"Price"`; invalid cursor 400; first-page `TotalCount > limit`; EF list uses `AsNoTracking` + `TagWith("products.list")` + SQL `Select` to `ProductDto`; MVC `POST /api/v2/Product/products` and GET Minimal API return the same ids; Dapper first page matches EF; Swagger v2 documents GET |

## Explicitly unsupported

| Non-goal | Expectation |
|----------|-------------|
| Offset / `Skip` | No public API |
| Dapper `ByShadow` | `PaginationErrorCode.ShadowNotSupported` |
| Dapper `IQueryable` | No overload; host supplies filter SQL |
| `IEnumerable` in-memory adapter | Out of this package; EF InMemory is tests only |
| `pageIndex` in cursor | `CursorRequest` has no page index |
| Raw / kitchen-sink hints (`UPDLOCK`, index names as strings) | Not on `PaginationOptions`. Allowlisted `QueryHint` only (`None`, `ReadUncommitted`). Host may still put `WITH (NOLOCK)` in Dapper SQL or chain EF `TagWith` / interceptors |
| LinqToDB nupkg | Not in this repo; a future adapter could reuse the same IR. Not packed in v1 |
| Dapper nupkg | In-repo project only. FeatureFusion showcases it locally; not packed in v1 |
| OData / filter DSL / Mongo | Out of v1 |
| Mediator / MCP package refs | Pagination does not reference them |
| Lab `Result<T>` inside the library | Host maps `CursorPage<T>` → `PagedResult<T>` |
| Testcontainers Postgres in CI | Assert Postgres **tuple SQL text**; **execute** against Sqlite |
| Sort whole value objects / `byte[]` / Ulid structs | `UnsupportedSortType`; sort the mapped scalar |

## Edge

Null/empty/whitespace cursor = first page. Empty cursor + `PageDirection.Backward` = last page. Empty `SigningKey` + signing requested → `SigningKeyRequired`. Invalid SQL identifier (`Name; DROP TABLE`) → `InvalidIdentifier`. Slot JSON that cannot decode → `InvalidCursor`. Unregistered registry enum → `InvalidOperationException`. `NullOrder` is seek-side for reference types only and does **not** emit `NULLS FIRST/LAST`. Nullable value-type sort slots are rejected (`NullableSortUnsupported`). DateTime cursor values are UTC. Guid CLR order ≠ SQL Server `uniqueidentifier`. Host `OrderBy` is replaced. No `IEnumerable` API.
