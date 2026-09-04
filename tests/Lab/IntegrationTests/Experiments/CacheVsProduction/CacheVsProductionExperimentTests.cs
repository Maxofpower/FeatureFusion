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

namespace IntegrationTests.Experiments.CacheVsProduction;

/// <summary>
/// Experiment 3 — Cache vs Production (HTTP Redis idempotency).
/// Originally characterized Lab order-create idempotency; now a regression/provenance
/// gate that the extracted <c>BuildingBlocks.Idempotency</c> filter preserves miss →
/// production vs hit → replay (Redis <c>IDistributedCache</c>, fingerprint off).
/// Not a cache-library unit test. Does not cover MCP <c>orders.create</c>
/// (in-memory <c>IMcpIdempotencyStore</c> — Exp 6). Does not prove fingerprinting (Exp 12)
/// or concurrency lock races (Exp 4).
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class CacheVsProductionExperimentTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public CacheVsProductionExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_output = output;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Order_create_redis_idempotency_cache_vs_production_is_observed()
	{
		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var cachedKey = System.Ulid.NewUlid().ToString();
		var controlKey = System.Ulid.NewUlid().ToString();
		var calls = new List<CacheVsProductionCall>();

		var miss = await SendAsync(
			capture,
			calls,
			behavior: "MissPopulate",
			idempotencyKey: cachedKey,
			quantity: 2);

		var hit = await SendAsync(
			capture,
			calls,
			behavior: "HitReplay",
			idempotencyKey: cachedKey,
			quantity: 2);

		var mutatedBody = await SendAsync(
			capture,
			calls,
			behavior: "SameKeyDifferentQuantity",
			idempotencyKey: cachedKey,
			quantity: 5);

		var freshProduction = await SendAsync(
			capture,
			calls,
			behavior: "NewKeyDifferentQuantity",
			idempotencyKey: controlKey,
			quantity: 5);

		var result = new CacheVsProductionExperimentResult(
			Name: "order-create-redis-idempotency-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new CacheVsProductionConfiguration(
				HttpOrderCreate.Path,
				HttpOrderCreate.IdempotencyHeader,
				HttpOrderCreate.CachedResponseHeader,
				Cache: "Redis IDistributedCache (BuildingBlocks.Idempotency IdempotentAttributeFilter, useLock: true)",
				Production: "OrderController.CreateOrder → ISender.Send(CreateOrderCommand) → catalog SaveChanges + outbox"),
			Calls: calls,
			Observations: new CacheVsProductionObservations(
				MissHttpStatus: miss.HttpStatus,
				MissOrderId: miss.OrderId,
				MissQuantity: miss.Quantity,
				MissCachedHeader: miss.CachedResponseHeader,
				MissMediatorSpans: miss.MediatorSpanCount,
				MissNpgsqlSpans: miss.NpgsqlSpanCount,
				MissRedisSpans: miss.RedisSpanCount,
				HitHttpStatus: hit.HttpStatus,
				HitOrderId: hit.OrderId,
				HitQuantity: hit.Quantity,
				HitCachedHeader: hit.CachedResponseHeader,
				HitMediatorSpans: hit.MediatorSpanCount,
				HitNpgsqlSpans: hit.NpgsqlSpanCount,
				HitRedisSpans: hit.RedisSpanCount,
				HitSameOrderIdAsMiss: hit.OrderId == miss.OrderId && miss.OrderId != Guid.Empty,
				MutatedBodyHttpStatus: mutatedBody.HttpStatus,
				MutatedBodyOrderId: mutatedBody.OrderId,
				MutatedBodyQuantity: mutatedBody.Quantity,
				MutatedBodyCachedHeader: mutatedBody.CachedResponseHeader,
				MutatedBodyKeptOriginalQuantity: mutatedBody.Quantity == miss.Quantity,
				FreshProductionHttpStatus: freshProduction.HttpStatus,
				FreshProductionOrderId: freshProduction.OrderId,
				FreshProductionQuantity: freshProduction.Quantity,
				FreshProductionCachedHeader: freshProduction.CachedResponseHeader,
				FreshProductionDifferentOrderId: freshProduction.OrderId != miss.OrderId && freshProduction.OrderId != Guid.Empty,
				Notes:
				[
					$"Miss TraceId={miss.TraceId}; sources={DescribeSources(capture.ForTraceHex(miss.TraceId))}.",
					$"Hit TraceId={hit.TraceId}; sources={DescribeSources(capture.ForTraceHex(hit.TraceId))}.",
					$"Same-key quantity=5 returned HTTP {mutatedBody.HttpStatus}, quantity={mutatedBody.Quantity}, cachedHeader={mutatedBody.CachedResponseHeader}.",
					$"New-key quantity=5 returned HTTP {freshProduction.HttpStatus}, orderId={freshProduction.OrderId}, quantity={freshProduction.Quantity}."
				]));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		miss.HttpStatus.Should().Be(200);
		miss.OrderId.Should().NotBeEmpty();
		miss.Quantity.Should().Be(2);
		miss.CachedResponseHeader.Should().BeFalse(
			"the first request with a new Idempotency-Key should execute production, not replay a cached body");
		miss.MediatorSpanCount.Should().BeGreaterThan(0,
			"cache miss should reach ISender.Send(CreateOrderCommand). Spans: {0}",
			Describe(capture.ForTraceHex(miss.TraceId)));
		miss.NpgsqlSpanCount.Should().BeGreaterThan(0,
			"cache miss should persist catalog/outbox via SaveChanges. Spans: {0}",
			Describe(capture.ForTraceHex(miss.TraceId)));

		hit.HttpStatus.Should().Be(200);
		hit.OrderId.Should().Be(miss.OrderId);
		hit.Quantity.Should().Be(2);
		hit.CachedResponseHeader.Should().BeTrue(
			"replay of a completed Idempotency-Key should set {0}", HttpOrderCreate.CachedResponseHeader);
		hit.MediatorSpanCount.Should().Be(0,
			"cache hit short-circuits before the controller action. Spans: {0}",
			Describe(capture.ForTraceHex(hit.TraceId)));
		hit.NpgsqlSpanCount.Should().Be(0,
			"cache hit should not re-run catalog SaveChanges. Spans: {0}",
			Describe(capture.ForTraceHex(hit.TraceId)));

		miss.Body.Should().Contain("\"orderId\"",
			"production MVC serialization is camelCase System.Text.Json");
		hit.Body.Should().Contain("\"OrderId\"",
			"the cached replay is System.Text.Json from BuildingBlocks.Idempotency, which defaults to PascalCase");

		mutatedBody.HttpStatus.Should().Be(200);
		mutatedBody.OrderId.Should().Be(miss.OrderId);
		mutatedBody.Quantity.Should().Be(2,
			"the Redis entry is keyed by user + Idempotency-Key, not by request body");
		mutatedBody.CachedResponseHeader.Should().BeTrue();
		mutatedBody.MediatorSpanCount.Should().Be(0);
		mutatedBody.NpgsqlSpanCount.Should().Be(0);

		freshProduction.HttpStatus.Should().Be(200);
		freshProduction.OrderId.Should().NotBe(miss.OrderId);
		freshProduction.Quantity.Should().Be(5);
		freshProduction.CachedResponseHeader.Should().BeFalse();
		freshProduction.MediatorSpanCount.Should().BeGreaterThan(0,
			"a new Idempotency-Key must run production again. Spans: {0}",
			Describe(capture.ForTraceHex(freshProduction.TraceId)));
		freshProduction.NpgsqlSpanCount.Should().BeGreaterThan(0);
	}

	private async Task<CacheVsProductionCall> SendAsync(
		InProcessActivityCapture capture,
		List<CacheVsProductionCall> calls,
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

		var call = new CacheVsProductionCall(
			calls.Count + 1,
			behavior,
			idempotencyKey,
			quantity,
			result.HttpStatus,
			result.OrderId,
			result.Quantity,
			result.TotalAmount,
			result.CachedResponseHeader,
			result.ClientDurationMs,
			result.TraceIdHex,
			result.Spans.Count(IsMediator),
			result.Spans.Count(IsNpgsql),
			result.Spans.Count(IsRedis),
			result.Body);

		calls.Add(call);
		return call;
	}

	private static bool IsRedis(CapturedActivity span)
		=> span.Source.Contains("Redis", StringComparison.OrdinalIgnoreCase)
			|| span.Source.Contains("StackExchange", StringComparison.OrdinalIgnoreCase);

	private static string Describe(IReadOnlyList<CapturedActivity> spans)
		=> string.Join("; ", spans.Select(s => $"{s.Source}:{s.DisplayName}"));

	private static string DescribeSources(IReadOnlyList<CapturedActivity> spans)
		=> string.Join(",", spans.Select(s => s.Source).Distinct().OrderBy(s => s, StringComparer.Ordinal));

	private sealed record CacheVsProductionCall(
		int RequestNumber,
		string Behavior,
		string IdempotencyKey,
		int RequestedQuantity,
		int HttpStatus,
		Guid OrderId,
		int? Quantity,
		decimal? TotalAmount,
		bool CachedResponseHeader,
		double ClientDurationMs,
		string TraceId,
		int MediatorSpanCount,
		int NpgsqlSpanCount,
		int RedisSpanCount,
		string Body);

	private sealed record CacheVsProductionConfiguration(
		string Path,
		string IdempotencyHeader,
		string CachedResponseHeader,
		string Cache,
		string Production);

	private sealed record CacheVsProductionObservations(
		int MissHttpStatus,
		Guid MissOrderId,
		int? MissQuantity,
		bool MissCachedHeader,
		int MissMediatorSpans,
		int MissNpgsqlSpans,
		int MissRedisSpans,
		int HitHttpStatus,
		Guid HitOrderId,
		int? HitQuantity,
		bool HitCachedHeader,
		int HitMediatorSpans,
		int HitNpgsqlSpans,
		int HitRedisSpans,
		bool HitSameOrderIdAsMiss,
		int MutatedBodyHttpStatus,
		Guid MutatedBodyOrderId,
		int? MutatedBodyQuantity,
		bool MutatedBodyCachedHeader,
		bool MutatedBodyKeptOriginalQuantity,
		int FreshProductionHttpStatus,
		Guid FreshProductionOrderId,
		int? FreshProductionQuantity,
		bool FreshProductionCachedHeader,
		bool FreshProductionDifferentOrderId,
		IReadOnlyList<string> Notes);

	private sealed record CacheVsProductionExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		CacheVsProductionConfiguration Configuration,
		IReadOnlyList<CacheVsProductionCall> Calls,
		CacheVsProductionObservations Observations);
}
