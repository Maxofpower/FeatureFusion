using StackExchange.Redis;

namespace BuildingBlocks.Mcp;

/// <summary>
/// MCP-owned Redis lock for distributed write idempotency (SET NX PX acquire, owner-checked delete).
/// Uses the host <see cref="IConnectionMultiplexer"/> (database 0). Not HTTP
/// <c>IIdempotencyLock</c> and not a generic distributed-lock package.
/// </summary>
public sealed class RedisMcpIdempotencyLock : IMcpIdempotencyLock
{
	private readonly IConnectionMultiplexer _multiplexer;

	/// <summary>Creates a lock over <paramref name="multiplexer"/> database 0.</summary>
	public RedisMcpIdempotencyLock(IConnectionMultiplexer multiplexer)
	{
		_multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
	}

	/// <inheritdoc />
	public async Task<bool> TryAcquireAsync(string key, string ownerToken, TimeSpan lease, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);
		if (lease <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(lease), lease, "Lease must be greater than zero.");

		cancellationToken.ThrowIfCancellationRequested();
		var database = _multiplexer.GetDatabase();
		var expiryMilliseconds = (int)lease.TotalMilliseconds;
		if (expiryMilliseconds < 1)
			throw new ArgumentOutOfRangeException(nameof(lease), lease, "Lease must be at least 1 millisecond.");

		const string script = """
			local result = redis.call('SET', KEYS[1], ARGV[1], 'NX', 'PX', ARGV[2])
			if result then
			    return true
			else
			    return false
			end
			""";

		var result = (bool)await database.ScriptEvaluateAsync(
			script,
			[key],
			[ownerToken, expiryMilliseconds]).ConfigureAwait(false);
		return result;
	}

	/// <inheritdoc />
	public async Task<bool> ReleaseAsync(string key, string ownerToken, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken);
		cancellationToken.ThrowIfCancellationRequested();
		var database = _multiplexer.GetDatabase();

		const string script = """
			if redis.call('get', KEYS[1]) == ARGV[1] then
			    return redis.call('del', KEYS[1])
			else
			    return 0
			end
			""";

		var result = (int)await database.ScriptEvaluateAsync(
			script,
			[key],
			[ownerToken]).ConfigureAwait(false);
		return result == 1;
	}
}
