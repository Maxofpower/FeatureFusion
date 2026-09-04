using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.Concurrency;

/// <summary>
/// Experiment 4 — Concurrency Race (HTTP Redis idempotency lock).
/// Originally proved the concurrent same-key race and Redis SET NX coordination;
/// now a regression/provenance gate for <c>RedisIdempotencyLock</c> +
/// <c>[Idempotent(useLock: true)]</c> on order create. Same key: one production
/// winner; siblings are replay / Processing 409 / lock-failure 500. Different keys
/// do not share a lock. ProcessingTtl lease overlap is Exp 15. Does not prove
/// fingerprinting (Exp 12), or MCP idempotency (Exp 6).
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class ConcurrencyRaceExperimentTests
{
	private const int SameKeyConcurrency = 3;
	private const int IndependentConcurrency = 2;
	private const int Quantity = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public ConcurrencyRaceExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_output = output;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Concurrent_order_creates_coordinate_same_key_and_run_independent_keys()
	{
		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var sameKey = System.Ulid.NewUlid().ToString();
		var independentKeys = Enumerable.Range(0, IndependentConcurrency)
			.Select(_ => System.Ulid.NewUlid().ToString())
			.ToArray();

		var coordinated = await StartTogether(
			SameKeyConcurrency,
			i => SendAsync(capture, "SameKeyConcurrent", sameKey, Quantity));
		NumberCalls(coordinated, start: 1);

		var independent = await StartTogether(
			IndependentConcurrency,
			i => SendAsync(capture, "DifferentKeyConcurrent", independentKeys[i], Quantity));
		NumberCalls(independent, start: SameKeyConcurrency + 1);

		var coordinatedProduction = coordinated.Where(IsProductionWinner).ToList();
		var coordinatedCached = coordinated.Where(IsCachedReplay).ToList();
		var coordinatedConflict = coordinated.Where(c => c.HttpStatus == 409).ToList();
		var coordinatedLockFail = coordinated.Where(c => c.HttpStatus == 500).ToList();
		var coordinatedOrderIds = coordinated
			.Where(c => c.HttpStatus == 200 && c.OrderId != Guid.Empty)
			.Select(c => c.OrderId)
			.Distinct()
			.ToList();
		var coordinatedMediatorCallers = coordinated.Count(c => c.MediatorSpanCount > 0);
		var coordinatedNpgsqlCallers = coordinated.Count(c => c.NpgsqlSpanCount > 0);

		var independentOrderIds = independent.Select(c => c.OrderId).Distinct().ToList();
		var independentMediatorCallers = independent.Count(c => c.MediatorSpanCount > 0);

		var result = new ConcurrencyRaceExperimentResult(
			Name: "order-create-idempotency-concurrency-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new ConcurrencyRaceConfiguration(
				HttpOrderCreate.Path,
				SameKeyConcurrency,
				IndependentConcurrency,
				Mechanism: "Redis SET NX lock around GetOrCreate of Idempotency_{user}_{key}, then Processing/Completed status in IDistributedCache"),
			Calls: [.. coordinated, .. independent],
			Observations: new ConcurrencyRaceObservations(
				CoordinatedStatuses: coordinated.Select(c => c.HttpStatus).ToArray(),
				CoordinatedProductionWinners: coordinatedProduction.Count,
				CoordinatedCachedReplays: coordinatedCached.Count,
				CoordinatedConflicts: coordinatedConflict.Count,
				CoordinatedLockFailures: coordinatedLockFail.Count,
				CoordinatedUniqueOrderIds: coordinatedOrderIds.Count,
				CoordinatedMediatorCallers: coordinatedMediatorCallers,
				CoordinatedNpgsqlCallers: coordinatedNpgsqlCallers,
				IndependentStatuses: independent.Select(c => c.HttpStatus).ToArray(),
				IndependentUniqueOrderIds: independentOrderIds.Count,
				IndependentMediatorCallers: independentMediatorCallers,
				Notes:
				[
					$"Same-key statuses [{string.Join(",", coordinated.Select(c => c.HttpStatus))}] roles [{string.Join(",", coordinated.Select(DescribeRole))}].",
					$"Same-key mediator callers={coordinatedMediatorCallers}; npgsql callers={coordinatedNpgsqlCallers}; unique orderIds={coordinatedOrderIds.Count}.",
					$"Independent statuses [{string.Join(",", independent.Select(c => c.HttpStatus))}] orderIds [{string.Join(",", independent.Select(c => c.OrderId))}].",
					.. coordinated.Select(c => $"Same-key #{c.RequestNumber} {DescribeRole(c)} TraceId={c.TraceId} sources={DescribeSources(capture.ForTraceHex(c.TraceId))}.")
				]));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		coordinated.Should().HaveCount(SameKeyConcurrency);
		coordinatedProduction.Should().ContainSingle(
			"exactly one same-key request should execute production (lock winner). Roles: {0}",
			string.Join(",", coordinated.Select(DescribeRole)));
		coordinatedOrderIds.Should().ContainSingle(
			"coordinated 200s must replay one order, not create two. Ids: {0}",
			string.Join(",", coordinatedOrderIds));
		coordinatedMediatorCallers.Should().Be(1,
			"Redis lock + Processing status should admit one CreateOrderCommand. Calls: {0}",
			DescribeCalls(coordinated));
		coordinatedNpgsqlCallers.Should().Be(1,
			"only the production winner should persist outbox/catalog. Calls: {0}",
			DescribeCalls(coordinated));
		coordinated.Where(c => !IsProductionWinner(c)).Should().OnlyContain(
			c => IsCachedReplay(c) || c.HttpStatus == 409 || c.HttpStatus == 500,
			"losers should be cached replay, in-flight 409, or lock-failure 500 — not a second production winner");

		independent.Should().OnlyContain(c => c.HttpStatus == 200);
		independent.Should().OnlyContain(c => !c.CachedResponseHeader);
		independentOrderIds.Should().HaveCount(IndependentConcurrency,
			"different Idempotency-Keys must not share a lock. Ids: {0}",
			string.Join(",", independentOrderIds));
		independentMediatorCallers.Should().Be(IndependentConcurrency,
			"independent keys should each run production. Calls: {0}",
			DescribeCalls(independent));
		independent.Should().OnlyContain(c => c.NpgsqlSpanCount > 0);
	}

	private static async Task<ConcurrencyRaceCall[]> StartTogether(
		int count,
		Func<int, Task<ConcurrencyRaceCall>> send)
	{
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var tasks = Enumerable.Range(0, count).Select(async i =>
		{
			await gate.Task;
			return await send(i);
		}).ToArray();
		gate.SetResult();
		return await Task.WhenAll(tasks);
	}

	private static void NumberCalls(ConcurrencyRaceCall[] calls, int start)
	{
		for (var i = 0; i < calls.Length; i++)
		{
			calls[i] = calls[i] with { RequestNumber = start + i };
		}
	}

	private async Task<ConcurrencyRaceCall> SendAsync(
		InProcessActivityCapture capture,
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

		return new ConcurrencyRaceCall(
			0,
			behavior,
			idempotencyKey,
			result.HttpStatus,
			result.OrderId,
			result.Quantity,
			result.CachedResponseHeader,
			result.ClientDurationMs,
			result.TraceIdHex,
			result.Spans.Count(IsMediator),
			result.Spans.Count(IsNpgsql),
			result.Body);
	}

	private static bool IsProductionWinner(ConcurrencyRaceCall call)
		=> call.HttpStatus == 200 && !call.CachedResponseHeader;

	private static bool IsCachedReplay(ConcurrencyRaceCall call)
		=> call.HttpStatus == 200 && call.CachedResponseHeader;

	private static string DescribeRole(ConcurrencyRaceCall call)
		=> call.HttpStatus switch
		{
			200 when call.CachedResponseHeader => "cached-replay",
			200 => "production-winner",
			409 => "in-flight-conflict",
			500 => "lock-failure",
			_ => $"http-{call.HttpStatus}"
		};

	private static string DescribeCalls(IEnumerable<ConcurrencyRaceCall> calls)
		=> string.Join("; ", calls.Select(c =>
			$"{DescribeRole(c)} status={c.HttpStatus} mediator={c.MediatorSpanCount} npgsql={c.NpgsqlSpanCount} order={c.OrderId}"));

	private static string DescribeSources(IReadOnlyList<CapturedActivity> spans)
		=> string.Join(",", spans.Select(s => s.Source).Distinct().OrderBy(s => s, StringComparer.Ordinal));

	private sealed record ConcurrencyRaceCall(
		int RequestNumber,
		string Behavior,
		string IdempotencyKey,
		int HttpStatus,
		Guid OrderId,
		int? Quantity,
		bool CachedResponseHeader,
		double ClientDurationMs,
		string TraceId,
		int MediatorSpanCount,
		int NpgsqlSpanCount,
		string Body);

	private sealed record ConcurrencyRaceConfiguration(
		string Path,
		int SameKeyConcurrency,
		int IndependentConcurrency,
		string Mechanism);

	private sealed record ConcurrencyRaceObservations(
		int[] CoordinatedStatuses,
		int CoordinatedProductionWinners,
		int CoordinatedCachedReplays,
		int CoordinatedConflicts,
		int CoordinatedLockFailures,
		int CoordinatedUniqueOrderIds,
		int CoordinatedMediatorCallers,
		int CoordinatedNpgsqlCallers,
		int[] IndependentStatuses,
		int IndependentUniqueOrderIds,
		int IndependentMediatorCallers,
		IReadOnlyList<string> Notes);

	private sealed record ConcurrencyRaceExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		ConcurrencyRaceConfiguration Configuration,
		IReadOnlyList<ConcurrencyRaceCall> Calls,
		ConcurrencyRaceObservations Observations);
}
