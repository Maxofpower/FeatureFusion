# BuildingBlocks.Domain.EntityFrameworkCore
Cached EF Core `ValueConverter` helpers for `BuildingBlocks.Domain` identities and value objects.

## Install
Not published to nuget.org. Add a project reference (requires `BuildingBlocks.Domain`):

```bash
dotnet add reference path/to/BuildingBlocks.Domain.EntityFrameworkCore.csproj
```

## Usage
```csharp
using BuildingBlocks.Domain.EntityFrameworkCore;
builder.Property(x => x.Id)
    .HasIdentityConversion<OrderId, int>(v => new OrderId(v));
builder.Property(x => x.Email)
    .HasValueObjectConversion(e => e.Value, Email.Create);
```
Converters are cached per CLR type pair so configurations share one instance.