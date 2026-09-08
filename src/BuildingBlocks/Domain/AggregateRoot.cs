namespace BuildingBlocks.Domain;

/// <summary>
/// Consistency boundary for a cluster of entities. Collects <see cref="IDomainEvent"/> instances
/// in memory; the application host is responsible for clearing and publishing them after persistence.
/// Use <see cref="Entity{TId}.CheckRule"/> for invariants that must hold before state changes.
/// </summary>
/// <typeparam name="TId">Identity type of the root.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregate<TId>
	where TId : notnull
{
	private readonly List<IDomainEvent> _domainEvents = [];

	/// <summary>For ORMs. Domain code should use factories.</summary>
	protected AggregateRoot()
	{
	}

	/// <summary>When the identity is known at construction.</summary>
	/// <param name="id">Aggregate identity.</param>
	protected AggregateRoot(TId id) : base(id)
	{
	}

	/// <inheritdoc />
	public long OriginalVersion { get; protected set; }

	/// <summary>Events raised since the last commit; not yet dispatched by the host.</summary>
	public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

	/// <summary>Enqueues a domain event. Duplicate <see cref="IDomainEvent.EventId"/> values are ignored.</summary>
	/// <param name="domainEvent">Event to enqueue.</param>
	protected void Raise(IDomainEvent domainEvent)
	{
		ArgumentNullException.ThrowIfNull(domainEvent);
		if (_domainEvents.Any(e => e.EventId == domainEvent.EventId))
			return;
		_domainEvents.Add(domainEvent);
	}

	/// <inheritdoc />
	public bool HasUncommittedDomainEvents() => _domainEvents.Count > 0;

	/// <inheritdoc />
	public IReadOnlyCollection<IDomainEvent> GetUncommittedDomainEvents() => DomainEvents;

	/// <inheritdoc />
	public void MarkUncommittedDomainEventsAsCommitted() => _domainEvents.Clear();

	/// <summary>Clears recorded events after the host has dispatched them. Same as <see cref="MarkUncommittedDomainEventsAsCommitted"/>.</summary>
	public void ClearDomainEvents() => MarkUncommittedDomainEventsAsCommitted();
}
