# BuildingBlocks.Pagination (IR)

Non-packable project: `SortKey`, opaque cursors, `CursorPage` / `CursorRequest`. **Not a NuGet.**

Shipped inside **BuildingBlocks.Pagination.EntityFrameworkCore** (`BuildingBlocks.Pagination.dll` is bundled in that nupkg). Optional `PaginationOptions.Hint` and the `--probe` benchmark live in that package — see [`../Pagination.EntityFrameworkCore/PACKAGE_README.md`](../Pagination.EntityFrameworkCore/PACKAGE_README.md).

![OFFSET skip growing with page number versus keyset seeking from the last Price and Id.](../../../docs/medium/images/04-offset-vs-keyset.png)
