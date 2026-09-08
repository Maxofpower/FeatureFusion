namespace BuildingBlocks.Domain;

/// <summary>
/// Strongly-typed identity wrapping a primitive. Different aggregates use different identity types
/// so a <c>ProductId</c> cannot be passed where an <c>OrderId</c> is required.
/// </summary>
/// <typeparam name="TId">Underlying primitive (int, long, Guid, string).</typeparam>
public interface IIdentity<out TId>
	where TId : notnull
{
	/// <summary>The wrapped primitive value.</summary>
	TId Value { get; }
}
