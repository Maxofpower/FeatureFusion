namespace BuildingBlocks.Idempotency;

/// <summary>
/// Optional distributed lock used only around GetOrCreate of the cache entry when
/// <see cref="AspNetCore.IdempotentAttribute.UseLock"/> or Minimal API <c>WithIdempotency(useLock: true)</c> is set.
/// </summary>
public interface IIdempotencyLock
{
	/// <summary>Attempts to acquire a lock. Returns false if another owner holds it.</summary>
	Task<bool> AcquireAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default);

	/// <summary>Releases the lock only when <paramref name="value"/> matches the owner.</summary>
	Task<bool> ReleaseAsync(string key, string value, CancellationToken cancellationToken = default);
}
