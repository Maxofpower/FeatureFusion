# BuildingBlocks.Pagination.Dapper — agent notes

**Not a NuGet.** `IDbConnection.QueryCursorPageAsync<T>(...)`. Project-reference `BuildingBlocks.Pagination`. **No** `IQueryable`.

Host SQL: `SELECT … FROM … WHERE …` with no `ORDER BY`/`OFFSET`/`FETCH`/`LIMIT` (`InvalidHostSql`). Hints such as `WITH (NOLOCK)` belong in host SQL. `PaginationOptions.Hint` default `None`; `ReadUncommitted` prefixes `SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;` on SQL Server only (`QueryHintSql`) — session isolation, not a table hint — then restores `READ COMMITTED` on the still-open connection; EF uses a transaction around COUNT+PAGE, not this SET prefix. Every slot needs `sql:`. Library wraps as `SELECT * FROM ({sql}) AS _bbp` and appends seek + `ORDER BY` + limit. Params: host + `@ks0`… + `@take`. Nullable value-type slots are rejected (`NullableSortUnsupported`). `NullOrder` emits `NULLS FIRST/LAST` on PostgreSQL and Sqlite ORDER BY (inverted on backward walks); unused on SQL Server. Postgres row comparison (`(a, b, …) >`) when all directions match — any column count. Mixed ASC/DESC stays OR.

`SqlDialect.PostgreSql` (tuple when directions match), `SqlServer` (`FETCH NEXT`), `Sqlite` (`LIMIT`). `ByShadow` is rejected.
