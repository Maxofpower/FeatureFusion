namespace BuildingBlocks.Domain;

/// <summary>Aggregate-root capabilities: domain events, version, and rule checks.</summary>
public interface IHaveAggregate : IHaveAggregateVersion
{
	/// <summary>True when at least one domain event has not been marked committed.</summary>
	bool HasUncommittedDomainEvents();

	/// <summary>Events raised since the last commit (host publishes, then marks committed).</summary>
	IReadOnlyCollection<IDomainEvent> GetUncommittedDomainEvents();

	/// <summary>Clears the in-memory event queue after the host has dispatched them.</summary>
	void MarkUncommittedDomainEventsAsCommitted();

	/// <summary>Validates <paramref name="rule"/> and throws when it is broken.</summary>
	void CheckRule(IBusinessRule rule);
}
