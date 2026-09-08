# BuildingBlocks.Domain.EntityFrameworkCore — agent notes
EF Core mapping helpers for `BuildingBlocks.Domain`. **In-repo only — not published to nuget.org.** Reference the project when the host uses EF.

## When to choose
You map `Identity<T>` / `ValueObject` columns and want **cached** `ValueConverter` instances (EF recommendation) plus fluent `HasIdentityConversion` / `HasValueObjectConversion`.
Do **not** put domain factories or business rules here.

## Surface
| Type | Role |
|------|------|
| `IdentityValueConverter.For` / `ForNullable` | Cached identity ↔ primitive converters |
| `ValueObjectValueConverter.For` | Cached VO ↔ primitive converters |
| `HasIdentityConversion` / `HasValueObjectConversion` | Fluent property helpers |

## Example
```csharp
builder.Property(p => p.Id)
    .HasIdentityConversion<ProductId, int>(v => new ProductId(v))
    .ValueGeneratedOnAdd();
builder.Property(p => p.BrandId)
    .HasIdentityConversion<BrandId, int>(v => new BrandId(v));
builder.Property(c => c.Email)
    .HasValueObjectConversion(e => e.Value, v => Email.Create(v));
```