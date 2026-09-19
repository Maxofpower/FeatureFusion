using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace BuildingBlocks.Mcp.Tests;

/// <summary>
/// <see cref="RedisMcpIdempotencyLock"/> SET NX PX acquire and owner-checked release.
/// In-memory Redis script stand-in — not HTTP <c>IIdempotencyLock</c>.
/// </summary>
public sealed class RedisMcpIdempotencyLockTests
{
	[Fact]
	public async Task Acquire_succeeds_when_key_is_absent()
	{
		var (lockObj, store) = Create();
		Assert.True(await lockObj.TryAcquireAsync("k", "owner-a", TimeSpan.FromSeconds(5), CancellationToken.None));
		Assert.Equal("owner-a", store.Owner("k"));
	}

	[Fact]
	public async Task Acquire_fails_when_another_owner_holds_the_key()
	{
		var (lockObj, _) = Create();
		Assert.True(await lockObj.TryAcquireAsync("k", "owner-a", TimeSpan.FromSeconds(30), CancellationToken.None));
		Assert.False(await lockObj.TryAcquireAsync("k", "owner-b", TimeSpan.FromSeconds(30), CancellationToken.None));
	}

	[Fact]
	public async Task Acquire_succeeds_after_lease_expiry()
	{
		var (lockObj, store) = Create();
		Assert.True(await lockObj.TryAcquireAsync("k", "owner-a", TimeSpan.FromMilliseconds(40), CancellationToken.None));
		await Task.Delay(80);
		Assert.True(await lockObj.TryAcquireAsync("k", "owner-b", TimeSpan.FromSeconds(5), CancellationToken.None));
		Assert.Equal("owner-b", store.Owner("k"));
	}

	[Fact]
	public async Task Release_succeeds_for_owner_and_fails_for_wrong_owner()
	{
		var (lockObj, store) = Create();
		Assert.True(await lockObj.TryAcquireAsync("k", "owner-a", TimeSpan.FromSeconds(30), CancellationToken.None));
		Assert.False(await lockObj.ReleaseAsync("k", "owner-b", CancellationToken.None));
		Assert.Equal("owner-a", store.Owner("k"));
		Assert.True(await lockObj.ReleaseAsync("k", "owner-a", CancellationToken.None));
		Assert.Null(store.Owner("k"));
	}

	[Fact]
	public async Task Canceled_token_throws_before_redis()
	{
		var (lockObj, store) = Create();
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			lockObj.TryAcquireAsync("k", "owner-a", TimeSpan.FromSeconds(5), cts.Token));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			lockObj.ReleaseAsync("k", "owner-a", cts.Token));
		Assert.Null(store.Owner("k"));
	}

	[Fact]
	public async Task Redis_error_propagates()
	{
		var mux = DispatchProxy.Create<IConnectionMultiplexer, ThrowingMux>();
		var lockObj = new RedisMcpIdempotencyLock(mux);
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			lockObj.TryAcquireAsync("k", "owner-a", TimeSpan.FromSeconds(5), CancellationToken.None));
	}

	[Fact]
	public void Constructor_rejects_null_multiplexer()
		=> Assert.Throws<ArgumentNullException>(() => new RedisMcpIdempotencyLock(null!));

	[Fact]
	public async Task Rejects_invalid_key_owner_and_lease()
	{
		var (lockObj, _) = Create();
		await Assert.ThrowsAsync<ArgumentException>(() =>
			lockObj.TryAcquireAsync(" ", "owner", TimeSpan.FromSeconds(1), CancellationToken.None));
		await Assert.ThrowsAsync<ArgumentException>(() =>
			lockObj.TryAcquireAsync("k", " ", TimeSpan.FromSeconds(1), CancellationToken.None));
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			lockObj.TryAcquireAsync("k", "owner", TimeSpan.Zero, CancellationToken.None));
	}

	[Fact]
	public void UseRedisLock_registers_mcp_lock_from_host_multiplexer()
	{
		var store = new InMemoryRedisLockStore();
		var database = DispatchProxy.Create<IDatabase, ScriptDatabase>();
		((ScriptDatabase)(object)database).Store = store;
		var connection = DispatchProxy.Create<IConnectionMultiplexer, ScriptMux>();
		((ScriptMux)(object)connection).Database = database;

		var services = new ServiceCollection();
		services.AddSingleton<IConnectionMultiplexer>(connection);
		services.AddDistributedMemoryCache();
		services.AddBuildingBlocksMcp(o =>
		{
			o.MapTool<ProbeWrite, object>(
				"tests.redis-lock",
				"Probe",
				(_, _, _) => Task.FromResult(McpResult.Ok<object>(new { })),
				a => a.Kind = McpToolKind.Query);
			o.UseDistributedIdempotency();
			o.UseRedisLock();
		});
		using var sp = services.BuildServiceProvider();
		Assert.IsType<RedisMcpIdempotencyLock>(sp.GetRequiredService<IMcpIdempotencyLock>());
		Assert.NotNull(sp.GetRequiredService<IMcpInvoker>());
	}

	private static (RedisMcpIdempotencyLock Lock, InMemoryRedisLockStore Store) Create()
	{
		var store = new InMemoryRedisLockStore();
		var database = DispatchProxy.Create<IDatabase, ScriptDatabase>();
		((ScriptDatabase)(object)database).Store = store;
		var mux = DispatchProxy.Create<IConnectionMultiplexer, ScriptMux>();
		((ScriptMux)(object)mux).Database = database;
		return (new RedisMcpIdempotencyLock(mux), store);
	}
}

internal sealed class InMemoryRedisLockStore
{
	private readonly ConcurrentDictionary<string, (string Owner, long ExpiresAtTicks)> _locks = new(StringComparer.Ordinal);

	public string? Owner(string key)
	{
		if (!_locks.TryGetValue(key, out var entry))
			return null;
		if (entry.ExpiresAtTicks <= DateTime.UtcNow.Ticks)
		{
			_locks.TryRemove(key, out _);
			return null;
		}

		return entry.Owner;
	}

	public bool TrySetNx(string key, string owner, int expiryMilliseconds)
	{
		var expires = DateTime.UtcNow.AddMilliseconds(expiryMilliseconds).Ticks;
		while (true)
		{
			if (_locks.TryGetValue(key, out var existing))
			{
				if (existing.ExpiresAtTicks > DateTime.UtcNow.Ticks)
					return false;
				if (_locks.TryUpdate(key, (owner, expires), existing))
					return true;
				continue;
			}

			if (_locks.TryAdd(key, (owner, expires)))
				return true;
		}
	}

	public bool TryRelease(string key, string owner)
	{
		if (!_locks.TryGetValue(key, out var existing))
			return false;
		if (existing.ExpiresAtTicks <= DateTime.UtcNow.Ticks)
		{
			_locks.TryRemove(key, out _);
			return false;
		}

		if (!string.Equals(existing.Owner, owner, StringComparison.Ordinal))
			return false;
		return _locks.TryRemove(key, out _);
	}
}

internal class ScriptDatabase : DispatchProxy
{
	public InMemoryRedisLockStore Store { get; set; } = new();

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		if (targetMethod?.Name == nameof(IDatabase.ScriptEvaluateAsync)
			&& args is { Length: >= 3 }
			&& args[0] is string script)
		{
			var keys = args[1] as RedisKey[] ?? [];
			var values = args[2] as RedisValue[] ?? [];
			return EvaluateAsync(script, keys, values);
		}

		throw new NotSupportedException(targetMethod?.Name);
	}

	private Task<RedisResult> EvaluateAsync(string script, RedisKey[] keys, RedisValue[] values)
	{
		var key = (string)keys[0]!;
		if (script.Contains("SET", StringComparison.Ordinal) && script.Contains("NX", StringComparison.Ordinal))
		{
			var owner = (string)values[0]!;
			var expiry = (int)values[1];
			var acquired = Store.TrySetNx(key, owner, expiry);
			return Task.FromResult(RedisResult.Create(acquired));
		}

		var released = Store.TryRelease(key, (string)values[0]!);
		return Task.FromResult(RedisResult.Create(released ? 1 : 0));
	}
}

internal class ScriptMux : DispatchProxy
{
	public IDatabase Database { get; set; } = null!;

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		if (targetMethod?.Name == nameof(IConnectionMultiplexer.GetDatabase))
			return Database;
		throw new NotSupportedException(targetMethod?.Name);
	}
}

internal class ThrowingMux : DispatchProxy
{
	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		=> throw new InvalidOperationException("redis-unavailable");
}
