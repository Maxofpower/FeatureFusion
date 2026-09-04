using StackExchange.Redis;

namespace BuildingBlocks.Idempotency.Redis;

/// <summary>
/// Redis SET NX / compare-and-delete lock matching the Lab EventBus-era lock scripts.
/// </summary>
public sealed class RedisIdempotencyLock : IIdempotencyLock
{
	private readonly IConnectionMultiplexer _multiplexer;

	/// <summary>Creates a lock that uses database 0 of <paramref name="multiplexer"/>.</summary>
	public RedisIdempotencyLock(IConnectionMultiplexer multiplexer)
	{
		_multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
	}

	/// <inheritdoc />
	public async Task<bool> AcquireAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var database = _multiplexer.GetDatabase();
		var expiryMilliseconds = (int)expiry.TotalMilliseconds;

		const string script = """
			local key = KEYS[1]
			local value = ARGV[1]
			local expiry = ARGV[2]

			local result = redis.call('SET', key, value, 'NX', 'PX', expiry)

			if result then
			    return true
			else
			    return false
			end
			""";

		var result = (bool)await database.ScriptEvaluateAsync(
			script,
			[key],
			[value, expiryMilliseconds]).ConfigureAwait(false);

		return result;
	}

	/// <inheritdoc />
	public async Task<bool> ReleaseAsync(string key, string value, CancellationToken cancellationToken = default)
	{
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
			[value]).ConfigureAwait(false);

		return result == 1;
	}
}
