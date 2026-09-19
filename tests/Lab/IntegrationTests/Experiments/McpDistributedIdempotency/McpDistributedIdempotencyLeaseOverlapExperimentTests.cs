using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;
using FeatureFusion.Features.Orders.Commands;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Mcp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using Xunit.Abstractions;

namespace IntegrationTests.Experiments.McpDistributedIdempotency;

/// <summary>
/// Characterization (not a safety claim): if the Redis lock lease expires while CreateOrderCommand
/// is still running, a second MCP client may execute production. No lease renewal in 1.1.0.
/// Isolated WithWebHostBuilder host; default Lab memory idempotency is unchanged.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpDistributedIdempotencyLeaseOverlapExperimentTests : IDisposable
{
	private const string ToolName = "orders.create";
	private static readonly TimeSpan TestLease = TimeSpan.FromSeconds(1);

	private readonly AspireFixture _fixture;
	private readonly WebApplicationFactory<Program> _factory;
	private readonly HttpClient _http;
	private readonly CreateOrderLeaseHoldGate _gate;
	private readonly ITestOutputHelper _output;

	public McpDistributedIdempotencyLeaseOverlapExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_gate = new CreateOrderLeaseHoldGate();
		_factory = fixture.WithWebHostBuilder(builder =>
		{
			builder.ConfigureTestServices(services =>
			{
				services.RemoveAll<IMcpIdempotencyStore>();
				services.AddSingleton(new McpIdempotencyOptions
				{
					Lease = TestLease,
					PayloadTtl = TimeSpan.FromHours(1),
					AcquireWaitBudget = TimeSpan.FromSeconds(10),
					PollDelay = TimeSpan.FromMilliseconds(20)
				});
				services.AddSingleton<IMcpIdempotencyStore>(sp =>
					new DistributedCacheIdempotencyStore(
						sp.GetRequiredService<IDistributedCache>(),
						TimeSpan.FromHours(1)));
				services.AddSingleton<IMcpIdempotencyLock>(sp =>
					new RedisMcpIdempotencyLock(sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>()));
				services.AddSingleton(_gate);
				services.RemoveAll<ICommandHandler<CreateOrderCommand, Result<OrderResponse>>>();
				services.AddScoped<CreateOrderCommandHandler>();
				services.AddScoped<ICommandHandler<CreateOrderCommand, Result<OrderResponse>>>(sp =>
					new GatedCreateOrderCommandHandler(
						sp.GetRequiredService<CreateOrderCommandHandler>(),
						sp.GetRequiredService<CreateOrderLeaseHoldGate>()));
			});
		});
		_http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
	}

	/// <summary>
	/// After the lock lease expires, a second confirmed call may run CreateOrder while the first is held.
	/// Records overlap; does not assert exactly-once.
	/// </summary>
	[Fact]
	public async Task Expired_lock_lease_may_admit_overlapping_create_order()
	{
		await _fixture.ResetLabObservationAsync();
		_gate.Reset();
		var key = System.Ulid.NewUlid().ToString();
		var args = new Dictionary<string, object?>
		{
			["productId"] = 1,
			["quantity"] = 2,
			["customerId"] = 1,
			[McpDefaults.IdempotencyKeyArgument] = key,
			[McpDefaults.ConfirmedArgument] = true
		};

		await using var mcp = await LabMcpClient.CreateJuly2026Async(_http);
		var firstTask = mcp.CallToolAsync(ToolName, args);
		await _gate.FirstEntered.WaitAsync(TimeSpan.FromSeconds(30));
		await Task.Delay(TestLease + TimeSpan.FromSeconds(1));

		await using var mcp2 = await LabMcpClient.CreateJuly2026Async(_http);
		var second = await mcp2.CallToolAsync(ToolName, args);
		_gate.ReleaseFirst.TrySetResult();
		var first = await firstTask.AsTask().WaitAsync(TimeSpan.FromSeconds(30));

		_output.WriteLine(
			$"handlerEntries={_gate.HandlerEntries}; firstError={first.IsError}; secondError={second.IsError}");
		_gate.HandlerEntries.Should().BeGreaterThanOrEqualTo(2,
			"lease expiry is allowed to overlap production; this experiment characterizes that window");
	}

	public void Dispose()
	{
		_http.Dispose();
		_factory.Dispose();
	}

	public sealed class CreateOrderLeaseHoldGate
	{
		private int _handlerEntries;
		private TaskCompletionSource _firstEntered = NewTcs();
		private TaskCompletionSource _releaseFirst = NewTcs();

		public Task FirstEntered => _firstEntered.Task;
		public TaskCompletionSource ReleaseFirst => _releaseFirst;
		public int HandlerEntries => Volatile.Read(ref _handlerEntries);

		public void Reset()
		{
			Volatile.Write(ref _handlerEntries, 0);
			_firstEntered = NewTcs();
			_releaseFirst = NewTcs();
		}

		public async Task WaitIfFirstAsync()
		{
			var n = Interlocked.Increment(ref _handlerEntries);
			if (n == 1)
			{
				_firstEntered.TrySetResult();
				await _releaseFirst.Task.ConfigureAwait(false);
			}
		}

		private static TaskCompletionSource NewTcs()
			=> new(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private sealed class GatedCreateOrderCommandHandler
		: ICommandHandler<CreateOrderCommand, Result<OrderResponse>>
	{
		private readonly CreateOrderCommandHandler _inner;
		private readonly CreateOrderLeaseHoldGate _gate;

		public GatedCreateOrderCommandHandler(CreateOrderCommandHandler inner, CreateOrderLeaseHoldGate gate)
		{
			_inner = inner;
			_gate = gate;
		}

		public async Task<Result<OrderResponse>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
		{
			await _gate.WaitIfFirstAsync().ConfigureAwait(false);
			return await _inner.Handle(request, cancellationToken).ConfigureAwait(false);
		}
	}
}
