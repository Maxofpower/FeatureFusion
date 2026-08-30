# BuildingBlocks.Pagination.Dapper

In-repo **project only** — not a NuGet package. FeatureFusion showcases it at `POST /api/v2/Product/products-dapper`.

Uses the Pagination IR project (`IsPackable=false`). Hosts that want NuGet pagination should use **BuildingBlocks.Pagination.EntityFrameworkCore**.

## Usage (lab / local project reference)

Host supplies **filter SQL** with **no** `ORDER BY` / `OFFSET` / `FETCH` / `LIMIT` (`InvalidHostSql` otherwise). Isolation hints (`WITH (NOLOCK)`) stay **inside** that SQL. Optional `PaginationOptions.Hint` defaults to `None`; `QueryHint.ReadUncommitted` prefixes a SQL Server `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED` batch (session isolation, not `WITH (NOLOCK)`; no-op on PostgreSQL/Sqlite), then restores `READ COMMITTED` on the still-open connection. COUNT SQL gets the same prefix. Every sort slot needs `sql:`:

```csharp
var page = await connection.QueryCursorPageAsync<Product>(
    new CursorRequest(cursor, 20),
    key,
    "SELECT Id, Name, Price FROM products WHERE Deleted = FALSE",
    SqlDialect.PostgreSql);
```
