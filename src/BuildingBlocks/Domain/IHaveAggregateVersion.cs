namespace BuildingBlocks.Domain;

/// <summary>
/// Optimistic-concurrency version captured when the aggregate was loaded.
/// Compare against the store on save to detect lost updates.
/// </summary>
public interface IHaveAggregateVersion
{
	/// <summary>
	/// Version last loaded from persistence (0 for a newly created aggregate).
	/// </summary>
	long OriginalVersion { get; }
}
