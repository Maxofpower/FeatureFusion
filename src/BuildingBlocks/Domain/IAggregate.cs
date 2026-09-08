namespace BuildingBlocks.Domain;

/// <summary>Aggregate root: entity plus uncommitted domain events and a concurrency version.</summary>
public interface IAggregate<out TId> : IEntity<TId>, IHaveAggregate
	where TId : notnull
{
}
