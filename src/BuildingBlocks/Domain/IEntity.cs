namespace BuildingBlocks.Domain;

/// <summary>Entity with typed identity and business-rule checks.</summary>
public interface IEntity<out TId> : IHaveIdentity<TId>
	where TId : notnull
{
	/// <summary>Validates <paramref name="rule"/> and throws when it is broken.</summary>
	void CheckRule(IBusinessRule rule);
}
