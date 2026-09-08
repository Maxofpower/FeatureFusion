namespace BuildingBlocks.Domain;

/// <summary>
/// Something that happened in the domain and should be dispatched by the host after persistence.
/// This package does not publish events — the application layer does.
/// </summary>
public interface IDomainEvent
{
	/// <summary>Unique event identifier (used to de-duplicate the in-memory queue).</summary>
	Guid EventId { get; }

	/// <summary>UTC timestamp when the event was raised.</summary>
	DateTime OccurredOnUtc { get; }

	/// <summary>Identity of the aggregate that raised the event, when known.</summary>
	object? AggregateId { get; }

	/// <summary>Aggregate version at the time the event was raised.</summary>
	long AggregateVersion { get; }
}
