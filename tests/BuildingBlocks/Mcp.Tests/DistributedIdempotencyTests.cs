using System.Text.Json;
using BuildingBlocks.Mcp.Catalog;
using BuildingBlocks.Mcp.Invocation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BuildingBlocks.Mcp.Tests;

/// <summary>
/// Distributed MCP idempotency: wait-and-replay with <see cref="IMcpIdempotencyLock"/>.
/// Not HTTP Processing/409. Lease expiry is characterized, not claimed as exactly-once.
/// </summary>
public sealed class DistributedIdempotencyTests
{
	private const string WriteTool = "tests.create";
	private const string ConfirmTool = "tests.confirm-write";

	/// <summary>Two invokers, shared store+lock, same key: one handler, both success, same payload.</summary>
	[Fact]
	public async Task Two_Invokers_Shared_Store_And_Lock_Replay_Same_Payload()
	{
		var (store, gate, sp) = CreateShared();
		var calls = 0;
		var a = CreateInvoker(sp, store, (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult(McpResult.Ok<object?>(new { Id = 41 }));
		});
		var b = CreateInvoker(sp, store, (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult(McpResult.Ok<object?>(new { Id = 99 }));
		});

		var args = WriteArgs("k-shared");
		var first = await a.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		var second = await b.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);

		Assert.True(first.IsSuccess);
		Assert.True(second.IsSuccess);
		Assert.Equal(1, calls);
		Assert.Contains("41", ((JsonElement)second.Value!).GetRawText(), StringComparison.Ordinal);
		Assert.Equal(1, gate.ReleaseCalls);
	}

	/// <summary>Concurrent same key: one InvokeCore while the lease is valid; waiters replay.</summary>
	[Fact]
	public async Task Concurrent_Same_Key_Waiters_Replay_Without_Second_InvokeCore()
	{
		var (store, gate, sp) = CreateShared();
		var calls = 0;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var invoker = CreateInvoker(sp, store, async (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			entered.TrySetResult();
			await release.Task;
			return McpResult.Ok<object?>(new { Id = 7 });
		});

		var args = WriteArgs("k-concurrent");
		var first = invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var secondTask = invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		await WaitUntilAsync(() => gate.AcquireAttempts >= 2, TimeSpan.FromSeconds(5));
		Assert.Equal(1, Volatile.Read(ref calls));
		release.TrySetResult();
		var results = await Task.WhenAll(first, secondTask);
		Assert.Equal(1, calls);
		Assert.All(results, r => Assert.True(r.IsSuccess));
	}

	/// <summary>Completed replay does not run the handler again.</summary>
	[Fact]
	public async Task Completed_Replay_Does_Not_Invoke_Handler()
	{
		var (store, gate, sp) = CreateShared();
		var calls = 0;
		var invoker = CreateInvoker(sp, store, (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult(McpResult.Ok<object?>(new { Id = 3 }));
		});
		var args = WriteArgs("k-replay");
		Assert.True((await invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None)).IsSuccess);
		Assert.True((await invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None)).IsSuccess);
		Assert.Equal(1, calls);
		Assert.Equal(1, gate.AcquireSuccesses);
	}

	/// <summary>Handler throw: no Set; a later call may execute.</summary>
	[Fact]
	public async Task Handler_Throw_Does_Not_Set_And_Later_Call_May_Execute()
	{
		var (store, _, sp) = CreateShared();
		var calls = 0;
		var invoker = CreateInvoker(sp, store, (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			if (calls == 1)
				throw new InvalidOperationException("boom");
			return Task.FromResult(McpResult.Ok<object?>(new { Id = 1 }));
		});
		var args = WriteArgs("k-throw");
		var first = await invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		Assert.Equal(McpErrorCode.Internal, first.Error!.Code);
		Assert.Null(await store.GetAsync(InvokerCacheKey("k-throw"), CancellationToken.None));
		var second = await invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		Assert.True(second.IsSuccess);
		Assert.Equal(2, calls);
	}

	/// <summary>Abandoned owner (vacated lock, no payload) lets another instance execute.</summary>
	[Fact]
	public async Task Abandoned_Owner_Expired_Lease_Allows_Another_Execution()
	{
		var (store, gate, sp) = CreateShared();
		var calls = 0;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var invoker = CreateInvoker(sp, store, async (_, _, _, _) =>
		{
			var n = Interlocked.Increment(ref calls);
			if (n == 1)
			{
				entered.TrySetResult();
				await release.Task;
			}

			return McpResult.Ok<object?>(new { Id = n });
		});

		var args = WriteArgs("k-abandon");
		var first = invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		gate.Vacate();
		var second = await invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		release.TrySetResult();
		await first;
		Assert.Equal(2, Volatile.Read(ref calls));
		Assert.True(second.IsSuccess);
	}

	/// <summary>Cancel while holding the lock: Release runs; a waiter is not stuck.</summary>
	[Fact]
	public async Task Cancel_While_Holding_Lock_Releases_And_Waiter_Proceeds()
	{
		var (store, gate, sp) = CreateShared();
		var calls = 0;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var invoker = CreateInvoker(sp, store, async (_, _, _, ct) =>
		{
			Interlocked.Increment(ref calls);
			entered.TrySetResult();
			await Task.Delay(TimeSpan.FromSeconds(30), ct);
			return McpResult.Ok<object?>(new { Id = 1 });
		});

		using var cts = new CancellationTokenSource();
		var args = WriteArgs("k-cancel");
		var first = invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, cts.Token);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await cts.CancelAsync();
		var canceled = await first;
		Assert.Equal(McpErrorCode.Canceled, canceled.Error!.Code);
		Assert.Equal(1, gate.ReleaseCalls);

		var second = await invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		Assert.True(second.IsSuccess);
		Assert.Equal(2, Volatile.Read(ref calls));
	}

	/// <summary>
	/// Lease-expiry overlap is allowed: vacating the lock while the first InvokeCore is running
	/// admits a second execution. Not exactly-once.
	/// </summary>
	[Fact]
	public async Task Lease_Expiry_Overlap_Allows_Two_Executions()
	{
		var (store, gate, sp) = CreateShared();
		var calls = 0;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var invoker = CreateInvoker(sp, store, async (_, _, _, _) =>
		{
			var n = Interlocked.Increment(ref calls);
			if (n == 1)
			{
				entered.TrySetResult();
				await release.Task;
			}

			return McpResult.Ok<object?>(new { Id = n });
		});

		var args = WriteArgs("k-overlap");
		var first = invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		gate.Vacate();
		var second = await invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		release.TrySetResult();
		var firstResult = await first;
		Assert.Equal(2, Volatile.Read(ref calls));
		Assert.True(firstResult.IsSuccess);
		Assert.True(second.IsSuccess);
	}

	/// <summary>Wrong owner cannot release; the original holder remains.</summary>
	[Fact]
	public async Task Wrong_Owner_Release_Returns_False_And_Leaves_Lock_Held()
	{
		var gate = new TestMcpIdempotencyLock();
		Assert.True(await gate.TryAcquireAsync("k", "owner-a", TimeSpan.FromMinutes(2), CancellationToken.None));
		Assert.False(await gate.ReleaseAsync("k", "owner-b", CancellationToken.None));
		Assert.False(await gate.TryAcquireAsync("k", "owner-c", TimeSpan.FromMinutes(2), CancellationToken.None));
		Assert.True(await gate.ReleaseAsync("k", "owner-a", CancellationToken.None));
		Assert.True(await gate.TryAcquireAsync("k", "owner-c", TimeSpan.FromMinutes(2), CancellationToken.None));
	}

	/// <summary>Lock acquire throw: Internal, handler never runs.</summary>
	[Fact]
	public async Task Acquire_Throw_Is_Internal_And_Does_Not_Invoke()
	{
		var (store, gate, sp) = CreateShared();
		gate.ThrowOnAcquire = true;
		var calls = 0;
		var invoker = CreateInvoker(sp, store, (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult(McpResult.Ok<object?>(1));
		});
		var result = await invoker.InvokeAsync(WriteTool, WriteArgs("k-lockfail"), McpInvokeContext.None, CancellationToken.None);
		Assert.Equal(McpErrorCode.Internal, result.Error!.Code);
		Assert.Equal(0, calls);
	}

	/// <summary>Store Get failure before invoke is Internal; Set failure after success still returns the computed result.</summary>
	[Fact]
	public async Task Cache_Get_Failure_Is_Internal_Set_Failure_After_Success_Returns_Computed()
	{
		var gate = new TestMcpIdempotencyLock();
		var throwing = new ThrowingStore();
		throwing.ThrowOnGet = true;
		var sp = BuildSp(throwing, gate);
		var calls = 0;
		var invoker = CreateInvoker(sp, throwing, (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult(McpResult.Ok<object?>(new { Id = 8 }));
		});
		var getFail = await invoker.InvokeAsync(WriteTool, WriteArgs("k-get"), McpInvokeContext.None, CancellationToken.None);
		Assert.Equal(McpErrorCode.Internal, getFail.Error!.Code);
		Assert.Equal(0, calls);

		throwing.ThrowOnGet = false;
		throwing.ThrowOnSet = true;
		var setFail = await invoker.InvokeAsync(WriteTool, WriteArgs("k-set"), McpInvokeContext.None, CancellationToken.None);
		Assert.True(setFail.IsSuccess);
		Assert.Equal(1, calls);
	}

	/// <summary>Different idempotency keys execute independently.</summary>
	[Fact]
	public async Task Different_Keys_Execute_Independently()
	{
		var (store, _, sp) = CreateShared();
		var calls = 0;
		var invoker = CreateInvoker(sp, store, (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult(McpResult.Ok<object?>(new { Id = calls }));
		});
		Assert.True((await invoker.InvokeAsync(WriteTool, WriteArgs("k1"), McpInvokeContext.None, CancellationToken.None)).IsSuccess);
		Assert.True((await invoker.InvokeAsync(WriteTool, WriteArgs("k2"), McpInvokeContext.None, CancellationToken.None)).IsSuccess);
		Assert.Equal(2, calls);
	}

	/// <summary>Queries never touch the store or lock.</summary>
	[Fact]
	public async Task Query_Does_Not_Touch_Store_Or_Lock()
	{
		var throwing = new ThrowingStore { ThrowOnGet = true, ThrowOnSet = true };
		var gate = new TestMcpIdempotencyLock { ThrowOnAcquire = true };
		var sp = BuildSp(throwing, gate);
		var listed = McpToolScanner.FromType(typeof(ListedOrder), (_, _, _, _) => Task.FromResult(McpResult.Ok<object?>("ok")));
		var invoker = new McpInvoker(
			[listed],
			sp,
			[],
			new NoOpRateLimiter(),
			new DefaultMcpResultMapper(),
			dispatcher: null,
			idempotency: throwing,
			resilience: null,
			telemetry: null,
			includeExceptionDetails: false);
		var result = await invoker.InvokeAsync(
			"tests.list",
			JsonDocument.Parse("""{"sku":"x"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.True(result.IsSuccess);
		Assert.Equal(0, gate.AcquireAttempts);
	}

	/// <summary>Unconfirmed RequireConfirmation returns ConfirmationRequired with no store/lock I/O.</summary>
	[Fact]
	public async Task Unconfirmed_Does_Not_Touch_Store_Or_Lock()
	{
		var throwing = new ThrowingStore { ThrowOnGet = true, ThrowOnSet = true };
		var gate = new TestMcpIdempotencyLock { ThrowOnAcquire = true };
		var sp = BuildSp(throwing, gate);
		var d = McpToolScanner.FromType(typeof(ConfirmWriteCommand), (_, _, _, _) => Task.FromResult(McpResult.Ok<object?>(1)));
		var invoker = new McpInvoker(
			[d],
			sp,
			[],
			new NoOpRateLimiter(),
			new DefaultMcpResultMapper(),
			dispatcher: null,
			idempotency: throwing,
			resilience: null,
			telemetry: null,
			includeExceptionDetails: false);
		var result = await invoker.InvokeAsync(
			ConfirmTool,
			JsonDocument.Parse("""{"qty":1,"idempotencyKey":"k-u"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.ConfirmationRequired, result.Error!.Code);
		Assert.Equal(0, gate.AcquireAttempts);
	}

	/// <summary>Confirmed accept then a second call replays the distributed payload.</summary>
	[Fact]
	public async Task Confirmed_Accept_Then_Distributed_Replay()
	{
		var (store, _, sp) = CreateShared();
		var calls = 0;
		var d = McpToolScanner.FromType(typeof(ConfirmWriteCommand), (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult(McpResult.Ok<object?>(new { Id = 12 }));
		});
		var invoker = new McpInvoker(
			[d],
			sp,
			[],
			new NoOpRateLimiter(),
			new DefaultMcpResultMapper(),
			dispatcher: null,
			idempotency: store,
			resilience: null,
			telemetry: null,
			includeExceptionDetails: false);
		var args = JsonDocument.Parse("""{"qty":1,"idempotencyKey":"k-ok","confirmed":true}""").RootElement;
		Assert.True((await invoker.InvokeAsync(ConfirmTool, args, McpInvokeContext.None, CancellationToken.None)).IsSuccess);
		var replay = await invoker.InvokeAsync(ConfirmTool, args, McpInvokeContext.None, CancellationToken.None);
		Assert.True(replay.IsSuccess);
		Assert.Equal(1, calls);
	}

	/// <summary>
	/// NEGATIVE: shared Get/Set without a lock (two invokers, process gates are not shared)
	/// allows two handlers — cache-only storage is not enough.
	/// </summary>
	[Fact]
	public async Task Shared_Store_Without_Lock_Allows_Two_Handlers()
	{
		var store = new MemoryIdempotencyStore();
		var calls = 0;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<McpResult<object?>> Handler(IServiceProvider _, object __, McpInvokeContext ___, CancellationToken ____)
		{
			var n = Interlocked.Increment(ref calls);
			if (n == 1)
			{
				entered.TrySetResult();
				return WaitAndOk(release);
			}

			return Task.FromResult(McpResult.Ok<object?>(new { Id = n }));
		}

		var empty = new ServiceCollection().BuildServiceProvider();
		var a = CreateInvoker(empty, store, Handler);
		var b = CreateInvoker(empty, store, Handler);
		var args = WriteArgs("k-neg");
		var first = a.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var second = await b.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None);
		release.TrySetResult();
		await first;
		Assert.Equal(2, Volatile.Read(ref calls));
		Assert.True(second.IsSuccess);
	}

	/// <summary>Cancel before acquire: Canceled; lock is never taken.</summary>
	[Fact]
	public async Task Cancel_Before_Acquire_Is_Canceled_And_Does_Not_Lock()
	{
		var (store, gate, sp) = CreateShared();
		var calls = 0;
		var invoker = CreateInvoker(sp, store, (_, _, _, _) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult(McpResult.Ok<object?>(1));
		});
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var result = await invoker.InvokeAsync(WriteTool, WriteArgs("k-pre"), McpInvokeContext.None, cts.Token);
		Assert.Equal(McpErrorCode.Canceled, result.Error!.Code);
		Assert.Equal(0, gate.AcquireAttempts);
		Assert.Equal(0, calls);
	}

	/// <summary>Release throw after a successful invoke must not turn success into failure.</summary>
	[Fact]
	public async Task Release_Failure_After_Success_Still_Returns_Computed()
	{
		var (store, gate, sp) = CreateShared();
		gate.ThrowOnRelease = true;
		var invoker = CreateInvoker(sp, store, (_, _, _, _) => Task.FromResult(McpResult.Ok<object?>(new { Id = 4 })));
		var result = await invoker.InvokeAsync(WriteTool, WriteArgs("k-rel"), McpInvokeContext.None, CancellationToken.None);
		Assert.True(result.IsSuccess);
		Assert.Equal(1, gate.ReleaseCalls);
	}

	/// <summary>Memory path (no IMcpIdempotencyLock): Exp 14-equivalent wait-and-replay on SemaphoreSlim.</summary>
	[Fact]
	public async Task Memory_Implementation_Preserves_Wait_And_Replay()
	{
		var store = new MemoryIdempotencyStore();
		var calls = 0;
		var empty = new ServiceCollection().BuildServiceProvider();
		var invoker = CreateInvoker(empty, store, async (_, _, _, _) =>
		{
			await Task.Delay(40);
			Interlocked.Increment(ref calls);
			return McpResult.Ok<object?>(new { Id = 1 });
		});
		var args = WriteArgs("k-mem");
		var results = await Task.WhenAll(
			invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None),
			invoker.InvokeAsync(WriteTool, args, McpInvokeContext.None, CancellationToken.None));
		Assert.Equal(1, calls);
		Assert.All(results, r => Assert.True(r.IsSuccess));
	}

	/// <summary>UseDistributedIdempotency fails fast when no <see cref="IMcpIdempotencyLock"/> is registered.</summary>
	[Fact]
	public void UseDistributedIdempotency_Without_Lock_Throws_On_Invoker_Resolve()
	{
		var services = new ServiceCollection();
		services.AddDistributedMemoryCache();
		services.AddBuildingBlocksMcp(o =>
		{
			o.MapTool<ProbeWrite, int>(
				"tests.dist",
				"Dist",
				(_, _, _) => Task.FromResult(McpResult.Ok(1)),
				a => a.Kind = McpToolKind.Command);
			o.UseDistributedIdempotency();
		});
		using var sp = services.BuildServiceProvider();
		var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IMcpInvoker>());
		Assert.Contains("IMcpIdempotencyLock", ex.Message, StringComparison.Ordinal);
	}

	/// <summary>Distributed store prefixes keys so they cannot collide with HTTP Idempotency_* entries.</summary>
	[Fact]
	public async Task Distributed_Store_Prefixes_Payload_Keys()
	{
		var services = new ServiceCollection();
		services.AddDistributedMemoryCache();
		await using var sp = services.BuildServiceProvider();
		var cache = sp.GetRequiredService<IDistributedCache>();
		var store = new DistributedCacheIdempotencyStore(cache, TimeSpan.FromMinutes(5));
		await store.SetAsync("orders.create\u001fk1", """{"id":1}""", CancellationToken.None);
		Assert.Equal("""{"id":1}""", await cache.GetStringAsync("mcp:idemp:orders.create\u001fk1"));
		Assert.Equal(
			"mcp:idemp:orders.create\u001fk1:lock",
			McpDefaults.FormatIdempotencyLockKey("orders.create", "k1"));
	}

	private static async Task<McpResult<object?>> WaitAndOk(TaskCompletionSource release)
	{
		await release.Task;
		return McpResult.Ok<object?>(new { Id = 1 });
	}

	private static (MemoryIdempotencyStore Store, TestMcpIdempotencyLock Gate, ServiceProvider Sp) CreateShared()
	{
		var store = new MemoryIdempotencyStore();
		var gate = new TestMcpIdempotencyLock();
		return (store, gate, BuildSp(store, gate));
	}

	private static ServiceProvider BuildSp(IMcpIdempotencyStore store, IMcpIdempotencyLock gate)
	{
		var services = new ServiceCollection();
		services.AddSingleton(store);
		services.AddSingleton(gate);
		services.AddSingleton<IMcpIdempotencyLock>(gate);
		services.AddSingleton(new McpIdempotencyOptions
		{
			Lease = TimeSpan.FromMinutes(2),
			AcquireWaitBudget = TimeSpan.FromSeconds(5),
			PollDelay = TimeSpan.FromMilliseconds(5)
		});
		return services.BuildServiceProvider();
	}

	private static McpInvoker CreateInvoker(
		IServiceProvider sp,
		IMcpIdempotencyStore store,
		Func<IServiceProvider, object, McpInvokeContext, CancellationToken, Task<McpResult<object?>>> handler)
	{
		var d = McpToolScanner.FromType(typeof(CreateListedOrder), handler);
		return new McpInvoker(
			[d],
			sp,
			[],
			new NoOpRateLimiter(),
			new DefaultMcpResultMapper(),
			dispatcher: null,
			idempotency: store,
			resilience: null,
			telemetry: null,
			includeExceptionDetails: true);
	}

	private static JsonElement WriteArgs(string key)
		=> JsonDocument.Parse($$"""{"qty":1,"idempotencyKey":"{{key}}"}""").RootElement;

	private static string InvokerCacheKey(string clientKey)
		=> WriteTool + "\u001f" + clientKey;

	private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
				return;
			await Task.Delay(5);
		}

		throw new TimeoutException("Condition was not met.");
	}
}

[McpTool("tests.confirm-write", Description = "Confirm write", Kind = McpToolKind.Command, Idempotent = true, RequireConfirmation = true)]
public sealed class ConfirmWriteCommand
{
	public int Qty { get; set; }
}

public sealed class ProbeWrite
{
	public int Qty { get; set; }
}

/// <summary>Deterministic in-process lock for package tests. Vacate simulates lease expiry or crash.</summary>
internal sealed class TestMcpIdempotencyLock : IMcpIdempotencyLock
{
	private readonly object _sync = new();
	private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

	public int AcquireAttempts;
	public int AcquireSuccesses;
	public int ReleaseCalls;
	public bool ThrowOnAcquire;
	public bool ThrowOnRelease;

	public void Vacate()
	{
		lock (_sync)
			_owners.Clear();
	}

	public Task<bool> TryAcquireAsync(string key, string ownerToken, TimeSpan lease, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (ThrowOnAcquire)
			throw new InvalidOperationException("lock-unavailable");

		lock (_sync)
		{
			Interlocked.Increment(ref AcquireAttempts);
			if (_owners.ContainsKey(key))
				return Task.FromResult(false);
			_owners[key] = ownerToken;
			Interlocked.Increment(ref AcquireSuccesses);
			return Task.FromResult(true);
		}
	}

	public Task<bool> ReleaseAsync(string key, string ownerToken, CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			Interlocked.Increment(ref ReleaseCalls);
			if (ThrowOnRelease)
				throw new InvalidOperationException("lock-release");
			if (!_owners.TryGetValue(key, out var owner) || !string.Equals(owner, ownerToken, StringComparison.Ordinal))
				return Task.FromResult(false);
			_owners.Remove(key);
			return Task.FromResult(true);
		}
	}
}

internal sealed class ThrowingStore : IMcpIdempotencyStore
{
	private readonly MemoryIdempotencyStore _inner = new();
	public bool ThrowOnGet;
	public bool ThrowOnSet;

	public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
	{
		if (ThrowOnGet)
			throw new InvalidOperationException("cache-get");
		return _inner.GetAsync(key, cancellationToken);
	}

	public Task SetAsync(string key, string payloadJson, CancellationToken cancellationToken)
	{
		if (ThrowOnSet)
			throw new InvalidOperationException("cache-set");
		return _inner.SetAsync(key, payloadJson, cancellationToken);
	}
}
