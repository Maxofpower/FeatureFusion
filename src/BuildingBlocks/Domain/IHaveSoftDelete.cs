namespace BuildingBlocks.Domain;

/// <summary>
/// Soft-delete marker. The host may apply a global query filter; this package does not.
/// </summary>
public interface IHaveSoftDelete
{
	/// <summary>True when the entity should be treated as deleted by the host.</summary>
	bool Deleted { get; }
}
