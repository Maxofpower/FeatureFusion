# BuildingBlocks.Domain — agent notes
Focused DDD primitives. **In-repo only — not published to nuget.org.** Reference `src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj`.
EF converters: companion `BuildingBlocks.Domain.EntityFrameworkCore` (also in-repo, not packed).

## When to choose this
You need `Entity` / `AggregateRoot` / `ValueObject` / typed `Identity` (`AggregateId` / `EntityId`) / `IBusinessRule` / `DomainException` without pulling an ORM, mediator, or event bus.
Do **not** use for CQRS messaging (Mediator), HTTP, or persistence mapping (use Domain.EntityFrameworkCore).

## Surface
| Type | Role |
|------|------|
| `Identity<TId>` | Strongly-typed id record + implicit to primitive |
| `AggregateId<TId>` / `EntityId<TId>` | Semantic id bases for aggregates vs child entities |
| `Entity<TId>` | Identity equality + `CheckRule`; **Id is protected set** |
| `AggregateRoot<TId>` | Entity + in-memory domain events (host publishes) |
| `ValueObject` | Structural equality via `GetEqualityComponents` |
| `IBusinessRule` + `BusinessRuleValidationException` | Invariant checks |
| `DomainException` | Factory / invariant failures |
No domain-event dispatcher. Host clears and publishes events.

## Example
```csharp
public sealed record OrderId : AggregateId<int>
{
    public OrderId(int value) : base(value) { }
    public static OrderId From(int value) => new(value);
    public static implicit operator OrderId(int value) => new(value);
}

public sealed class Order : AggregateRoot<OrderId>
{
    public string Number { get; private set; } = "";
    private Order() { }
    public static Order Create(OrderId id, string number) { /* ... */ }
}
```
