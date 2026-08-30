# BuildingBlocks.Pagination — agent notes

IR only: `SortKey`, `CursorCodec`, `CursorPage`. **IsPackable=false.** Do not `dotnet pack` this project.

Consumers install `BuildingBlocks.Pagination.EntityFrameworkCore`. Dapper is a sibling **project**, not a nupkg.

## Rules

- No EF/Dapper/ASP.NET/Mediator refs.
- Dapper (when used in-repo) needs `sql:` on every slot.
- `ByShadow<TValue>` is EF-only. Dapper throws `ShadowNotSupported`.
- Hosts map `PaginationException.Code` to HTTP 400.
- No `pageIndex`, no `Skip`/`OFFSET` API.
- Sort a mapped scalar. Do not `By` a value object, `byte[]`, or navigation.
- Do not put `NOLOCK` / isolation on `PaginationOptions`.
