# BuildingBlocks.Domain
Focused DDD primitives for .NET 8/9/10: **Entity**, **AggregateRoot**, **ValueObject**, **Identity** / **AggregateId** / **EntityId**, **IBusinessRule**, **DomainException**.
No ORM, mediator, or messaging dependencies. For EF Core converters use `BuildingBlocks.Domain.EntityFrameworkCore`.

## Install
Not published to nuget.org. Add a project reference:

```bash
dotnet add reference path/to/BuildingBlocks.Domain.csproj
```

## Quick start
```csharp
using BuildingBlocks.Domain;

public sealed record CustomerId : AggregateId<int>
{
    public CustomerId(int value) : base(value)
    {
        if (value < 0) throw new DomainException("Invalid id.");
    }
    public static implicit operator CustomerId(int value) => new(value);
}

public sealed class Customer : AggregateRoot<CustomerId>
{
    public Email Email { get; private set; } = null!;
    private Customer() { } // EF
    public static Customer Create(CustomerId id, Email email)
    {
        return new Customer { Id = id, Email = email };
    }
}

public sealed class Email : ValueObject
{
    public string Value { get; }
    private Email(string value) => Value = value;
    public static Email Create(string value) { /* validate */ return new Email(value.Trim()); }
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value.ToLowerInvariant();
    }
}
```

## Design notes
- Prefer **private setters** and factories on host entities.
- `AggregateRoot` stores events in memory; your application layer dispatches them.
- `Entity.Id` is **protected set** — assign in constructors/factories only.
- Keep this package small — do not add repositories, UnitOfWork, or outbox here.
