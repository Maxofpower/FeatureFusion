# BuildingBlocks.Pagination.EntityFrameworkCore — agent notes

**The** pagination NuGet. The IR (intermediate representation: `SortKey`, cursors) is a non-packable sibling, bundled into this nupkg. Dapper is a lab project, not a package.

Layout matches EF Core packages (`src/EFCore/Extensions`, `Query/Internal`, `Infrastructure`):

```text
Extensions/EntityFrameworkCursorExtensions.cs   # ToCursorPageAsync / ToCursorPageMappedAsync
Extensions/PaginationServiceCollectionExtensions.cs  # AddBuildingBlocksPagination / UseBuildingBlocksPagination
Extensions/KeysetIndexExtensions.cs             # HasKeysetIndex (+ optional NullOrder)
Query/Internal/                                 # CursorOrder, CursorSeek, CursorSeekTuple, CursorSlot
Infrastructure/Internal/CursorDbContext.cs      # ICurrentDbContext + shadow
Infrastructure/Internal/QueryHintExecutor.cs    # SQL Server ReadUncommitted txn wrap; else no-op
Infrastructure/Internal/PaginationNullsInterceptor.cs  # ORDER BY NULLS FIRST/LAST (tagged commands)
Infrastructure/Internal/CursorProvider.cs       # soft Npgsql/Sqlite/SqlServer detection
Infrastructure/Internal/NpgsqlIndexNullSort.cs  # soft HasNullSortOrder (HasKeysetIndex NullOrder)
```

Mapped properties: expressions. Shadow: `ByShadow<TValue>("Name")` → `EF.Property<TValue>`. No `OrderBy(string)`.

`ToCursorPageAsync` + `Expression<Func<T,TDto>>` projects **after** seek (prefer this). In-memory map: `ToCursorPageMappedAsync`. `HasKeysetIndex` builds the matching composite index from the same `SortKey` (optional `NullOrder` for Npgsql null-sort metadata).

Npgsql hosts: register `AddBuildingBlocksPagination` + `UseBuildingBlocksPagination` for `ORDER BY NULLS …` (LINQ cannot emit NULLS; a tagged interceptor `BuildingBlocks.Pagination:First|Last` is index-honest — not `AsyncLocal`, do not CASE-wrap OrderBy). Uniform non-nullable multi-column keys use row-comparison seek when Npgsql is loaded (any width: 2–8 via `ValueTuple.Create`, 9+ nested `TRest`; mixed ASC/DESC and `string` stay OR).

`Take(limit+1)`; reverse in memory when walking backward. `IncludeTotalCount` runs `CountAsync` **before** seek. Host applies `AsNoTracking`/`TagWith` before the extension. Host `OrderBy` is **replaced** by `CursorOrder.Apply`, not merged. `PaginationOptions.Hint` default `None`; `ReadUncommitted` is session isolation (not `WITH (NOLOCK)`): EF begins one SQL Server transaction around count+fetch when there is no ambient transaction; after commit/rollback the still-open connection is restored to `READ COMMITTED`; ambient is ignored; PostgreSQL/Sqlite no-op. There is no `IEnumerable` adapter. Nullable `T?` sort slots are rejected (`NullableSortUnsupported`). Guid CLR order ≠ SQL Server `uniqueidentifier` order. Bool/enum seek converts to the underlying numeric type. Guid seek uses `>` / `<`; string seek uses `string.Compare` (EF translates to SQL `>`). OrderBy lambdas are cached per fingerprint.

Promote with Default-job BDN or the SQLite **SQL** `--probe` numbers in PACKAGE_README (not EF InMemory, not `--job Dry`, not the 5k in-memory micro job). Set `SigningKey` on public HTTP APIs.
