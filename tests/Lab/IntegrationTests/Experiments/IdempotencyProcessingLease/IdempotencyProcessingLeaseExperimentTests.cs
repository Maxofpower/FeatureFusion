using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Idempotency;
using BuildingBlocks.Mediator;
using FeatureFusion.Features.Orders.Commands;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Xunit.Abstractions;
using static FeatureFusion.Features.Orders.Commands.CreateOrderCommandHandler;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.IdempotencyProcessingLease;

/// <summary>
/// Experiment 15 — HTTP idempotency <c>ProcessingTtl</c> lease overlap.
/// Hypothesis: if the first same-key request remains in <c>Processing</c> longer than
/// <c>ProcessingTtl</c>, a second request may acquire the expired lease and execute
/// production concurrently (duplicate order / outbox). Does not change Lab defaults —
/// short TTL and a test-only handler gate are applied only on this experiment host.
/// Does not add lease renewal. Completes the HTTP idempotency matrix after Exp 3/4/12.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class IdempotencyProcessingLeaseExperimentTests
{
	private const string UserIdFallback = "123";
	private const int Quantity = 2;

	/// <summary>Test-host only. Lab / package default remains 2 minutes.</summary>
	private static readonly TimeSpan TestProcessingTtl = TimeSpan.FromSeconds(1);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly WebApplicationFactory<Program> _factory;
	private readonly HttpClient _http;
	private readonly CreateOrderLeaseHoldGate _gate;
	private readonly ITestOutputHelper _output;

	public IdempotencyProcessingLeaseExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;

		_factory = fixture.WithWebHostBuilder(builder =>
		{
			builder.ConfigureTestServices(services =>
			{
				services.PostConfigure<IdempotencyOptions>(o =>
				{
					o.ProcessingTtl = TestProcessingTtl;
				});

				services.RemoveAll<ICommandHandler<CreateOrderCommand, Result<OrderResponse>>>();
				services.AddSingleton<CreateOrderLeaseHoldGate>();
				services.AddScoped<CreateOrderCommandHandler>();
				services.AddScoped<ICommandHandler<CreateOrderCommand, Result<OrderResponse>>>(sp =>
					new GatedCreateOrderCommandHandler(
						sp.GetRequiredService<CreateOrderCommandHandler>(),
						sp.GetRequiredService<CreateOrderLeaseHoldGate>()));
			});
		});

		_http = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
		_gate = _factory.Services.GetRequiredService<CreateOrderLeaseHoldGate>();
	}

	[Fact]
	public async Task Same_key_after_ProcessingTtl_may_run_production_while_first_still_in_flight()
	{
		_fixture.ProcessedEvents.Clear();
		_gate.Reset();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var key = System.Ulid.NewUlid().ToString();
		var cacheKey = $"Idempotency_{UserIdFallback}_{key}";
		var calls = new List<LeaseCall>();
		var timeline = new List<TimelineEvent>();

		void Mark(string phase, string detail)
			=> timeline.Add(new TimelineEvent(DateTimeOffset.UtcNow, phase, detail));

		var firstTask = SendAsync(capture, calls, "FirstLeaseHolder", key, Quantity);

		await _gate.FirstEntered.WaitAsync(TimeSpan.FromSeconds(30));
		var leaseAnchorUtc = DateTimeOffset.UtcNow;
		Mark("first_handler_entered",
			$"HandlerEntries={_gate.HandlerEntries}; Processing cache should be live until ~{leaseAnchorUtc.Add(TestProcessingTtl):O}.");

		var probe = await SendAsync(capture, calls, "ProcessingProbeWhileHeld", key, Quantity);
		Mark("processing_probe",
			$"HttpStatus={probe.HttpStatus}; expected Conflict while Processing unexpired.");

		var lease = await WaitForProcessingLeaseExpiredAsync(cacheKey, leaseAnchorUtc);
		Mark("lease_expired",
			$"CachePresent={lease.CachePresent}; Status={lease.Status}; ExpiresAt={lease.ProcessingExpiresAtUtc}; WaitedMs={lease.WaitedMs:F0}.");

		var second = await SendAsync(capture, calls, "ExpiredLeaseAcquirer", key, Quantity);
		Mark("second_completed",
			$"HttpStatus={second.HttpStatus}; OrderId={second.OrderId}; HandlerEntries={_gate.HandlerEntries}; Mediator={second.MediatorSpanCount}.");

		_gate.ReleaseFirst.TrySetResult();
		var first = await firstTask.WaitAsync(TimeSpan.FromSeconds(30));
		Mark("first_completed",
			$"HttpStatus={first.HttpStatus}; OrderId={first.OrderId}; HandlerEntries={_gate.HandlerEntries}.");

		var orderIds = new[] { first.OrderId, second.OrderId }
			.Where(id => id != Guid.Empty)
			.Distinct()
			.ToList();

		var outboxByOrder = new Dictionary<Guid, int>();
		foreach (var orderId in orderIds)
			outboxByOrder[orderId] = (await OrderOutboxObserver.FindByOrderIdAsync(_factory.Services, orderId)).Count;

		var finalCache = await ReadCacheEntryAsync(cacheKey);
		var productionCallers = calls.Count(c =>
			c.Behavior is "FirstLeaseHolder" or "ExpiredLeaseAcquirer"
			&& c.HttpStatus == 200
			&& c.MediatorSpanCount > 0
			&& !c.CachedResponseHeader);

		var hypothesisConfirmed =
			probe.HttpStatus == 409
			&& first.HttpStatus == 200
			&& second.HttpStatus == 200
			&& orderIds.Count == 2
			&& _gate.HandlerEntries >= 2
			&& productionCallers == 2;

		var result = new
		{
			name = "order-create-idempotency-processing-lease-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			environment = "Development",
			configuration = new
			{
				path = HttpOrderCreate.Path,
				testHostProcessingTtl = TestProcessingTtl.ToString(),
				labDefaultProcessingTtlUnchanged = "00:02:00",
				delayMechanism = "Test-only GatedCreateOrderCommandHandler awaits ReleaseFirst after FirstEntered; production CreateOrderCommandHandler unchanged",
				cacheKey
			},
			timeline,
			calls,
			observations = new
			{
				probeHttpStatus = probe.HttpStatus,
				firstHttpStatus = first.HttpStatus,
				secondHttpStatus = second.HttpStatus,
				firstOrderId = first.OrderId,
				secondOrderId = second.OrderId,
				distinctOrderIds = orderIds.Count,
				handlerEntries = _gate.HandlerEntries,
				productionCallers,
				outboxRowsByOrderId = outboxByOrder,
				finalCacheStatus = finalCache?.Status,
				finalCacheHasResponse = !string.IsNullOrEmpty(finalCache?.Response),
				hypothesisConfirmed,
				hypothesis = "Expired ProcessingTtl allows a second same-key production execution while the first is still in flight."
			},
			notes = new[]
			{
				"Probe while first held must be HTTP 409 Processing conflict (unexpired lease).",
				"After ProcessingTtl, ExpiredLeaseAcquirer may run production concurrently with FirstLeaseHolder.",
				"Package lock covers GetOrCreate only — not the full handler duration (documented lease tradeoff).",
				hypothesisConfirmed
					? "Hypothesis CONFIRMED: two production executions / distinct orderIds under expired Processing lease."
					: "Hypothesis FALSIFIED or inconclusive: overlap did not produce two production outcomes as packaged semantics allow."
			}
		};

		_output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, JsonOptions));

		probe.HttpStatus.Should().Be(409,
			"while the first request is held in Processing and the lease is unexpired, the same key must Conflict");
		probe.MediatorSpanCount.Should().Be(0);
		probe.CachedResponseHeader.Should().BeFalse();

		first.HttpStatus.Should().Be(200);
		first.OrderId.Should().NotBeEmpty();
		first.CachedResponseHeader.Should().BeFalse();
		first.MediatorSpanCount.Should().BeGreaterThan(0);

		second.HttpStatus.Should().Be(200,
			"after ProcessingTtl expiry the filter must admit a new production execution (ExpiredProcessing_AllowsNewExecution)");
		second.OrderId.Should().NotBeEmpty();
		second.CachedResponseHeader.Should().BeFalse(
			"second request should not be a Completed replay of the first (first still in flight)");
		second.MediatorSpanCount.Should().BeGreaterThan(0,
			"expired lease acquirer must execute production. Spans: {0}",
			Describe(capture.ForTraceHex(second.TraceId)));

		orderIds.Should().HaveCount(2,
			"concurrent production under expired Processing must create two distinct orders. first={0} second={1}",
			first.OrderId, second.OrderId);
		_gate.HandlerEntries.Should().BeGreaterThanOrEqualTo(2);
		productionCallers.Should().Be(2);

		foreach (var orderId in orderIds)
		{
			outboxByOrder[orderId].Should().BeGreaterThanOrEqualTo(1,
				"each production order should leave at least one outbox row. orderId={0}", orderId);
		}
	}

	private async Task<LeaseObservation> WaitForProcessingLeaseExpiredAsync(
		string cacheKey,
		DateTimeOffset leaseAnchorUtc)
	{
		var cache = _factory.Services.GetRequiredService<IDistributedCache>();
		var deadline = leaseAnchorUtc.Add(TestProcessingTtl).Add(TimeSpan.FromSeconds(5));
		var stopwatch = Stopwatch.StartNew();

		while (DateTimeOffset.UtcNow < deadline)
		{
			var entry = await ReadCacheEntryAsync(cache, cacheKey);
			var expiredByClock = DateTimeOffset.UtcNow >= leaseAnchorUtc.Add(TestProcessingTtl);
			var expiredByEntry = entry is null
				|| (entry.Status == "Processing"
					&& entry.ProcessingExpiresAtUtc is { } expires
					&& expires <= DateTimeOffset.UtcNow);

			if (expiredByClock && expiredByEntry)
			{
				return new LeaseObservation(
					entry is not null,
					entry?.Status,
					entry?.ProcessingExpiresAtUtc,
					stopwatch.Elapsed.TotalMilliseconds);
			}

			await Task.Delay(50);
		}

		var final = await ReadCacheEntryAsync(cache, cacheKey);
		return new LeaseObservation(
			final is not null,
			final?.Status,
			final?.ProcessingExpiresAtUtc,
			stopwatch.Elapsed.TotalMilliseconds);
	}

	private Task<IdempotencyCacheEntry?> ReadCacheEntryAsync(string cacheKey)
		=> ReadCacheEntryAsync(_factory.Services.GetRequiredService<IDistributedCache>(), cacheKey);

	private static async Task<IdempotencyCacheEntry?> ReadCacheEntryAsync(
		IDistributedCache cache,
		string cacheKey)
	{
		var bytes = await cache.GetAsync(cacheKey);
		if (bytes is null || bytes.Length == 0)
			return null;

		return JsonConvert.DeserializeObject<IdempotencyCacheEntry>(Encoding.UTF8.GetString(bytes));
	}

	private async Task<LeaseCall> SendAsync(
		InProcessActivityCapture capture,
		List<LeaseCall> calls,
		string behavior,
		string idempotencyKey,
		int quantity)
	{
		var result = await HttpOrderCreate.PostAsync(
			_http,
			capture,
			idempotencyKey,
			quantity,
			jsonOptions: JsonOptions);

		var call = new LeaseCall(
			Behavior: behavior,
			IdempotencyKey: idempotencyKey,
			HttpStatus: result.HttpStatus,
			OrderId: result.OrderId,
			CachedResponseHeader: result.CachedResponseHeader,
			MediatorSpanCount: result.Spans.Count(IsMediator),
			NpgsqlSpanCount: result.Spans.Count(IsNpgsql),
			TraceId: result.TraceIdHex,
			ClientDurationMs: result.ClientDurationMs,
			Body: result.Body);

		calls.Add(call);
		return call;
	}

	private static string Describe(IReadOnlyList<CapturedActivity> spans)
		=> string.Join("; ", spans.Select(s => $"{s.Source}:{s.DisplayName}"));

	/// <summary>Test-only synchronization. Not production code.</summary>
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

		public GatedCreateOrderCommandHandler(
			CreateOrderCommandHandler inner,
			CreateOrderLeaseHoldGate gate)
		{
			_inner = inner;
			_gate = gate;
		}

		public async Task<Result<OrderResponse>> Handle(
			CreateOrderCommand request,
			CancellationToken cancellationToken)
		{
			await _gate.WaitIfFirstAsync().ConfigureAwait(false);
			return await _inner.Handle(request, cancellationToken).ConfigureAwait(false);
		}
	}

	private sealed record LeaseCall(
		string Behavior,
		string IdempotencyKey,
		int HttpStatus,
		Guid OrderId,
		bool CachedResponseHeader,
		int MediatorSpanCount,
		int NpgsqlSpanCount,
		string TraceId,
		double ClientDurationMs,
		string Body);

	private sealed record TimelineEvent(DateTimeOffset AtUtc, string Phase, string Detail);

	private sealed record LeaseObservation(
		bool CachePresent,
		string? Status,
		DateTimeOffset? ProcessingExpiresAtUtc,
		double WaitedMs);
}
