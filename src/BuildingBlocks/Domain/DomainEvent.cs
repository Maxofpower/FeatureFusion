namespace BuildingBlocks.Domain;

/// <summary>
/// Base record for domain events. Subclass per event type; the host publishes after persistence.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
	/// <inheritdoc />
	public Guid EventId { get; init; } = Guid.NewGuid();

	/// <inheritdoc />
	public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

	/// <inheritdoc />
	public object? AggregateId { get; init; }

	/// <inheritdoc />
	public long AggregateVersion { get; init; }
}
