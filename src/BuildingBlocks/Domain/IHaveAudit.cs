namespace BuildingBlocks.Domain;

/// <summary>Optional creation audit. Hosts choose the user-id type.</summary>
public interface IHaveCreator<TUserId>
{
	/// <summary>UTC creation timestamp.</summary>
	DateTime CreatedAt { get; }

	/// <summary>Who created the entity, when the host tracks it.</summary>
	TUserId? CreatedBy { get; }
}

/// <summary>Optional modification audit.</summary>
public interface IHaveAudit<TUserId> : IHaveCreator<TUserId>
{
	/// <summary>UTC last-modified timestamp, when known.</summary>
	DateTime? LastModifiedAt { get; }

	/// <summary>Who last modified the entity, when the host tracks it.</summary>
	TUserId? LastModifiedBy { get; }
}
