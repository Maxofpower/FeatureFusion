using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Idempotency;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.IdempotencyFingerprint;

/// <summary>
/// Experiment 12 — Request fingerprint (opt-in, richer BuildingBlocks.Idempotency v1).
/// Proves <see cref="IdempotencyOptions.EnableRequestFingerprint"/>: same key + same
/// body replays; same key + different body â†’ 422 without production. Enabled only via
/// test-host <c>PostConfigure</c>; Lab default remains fingerprint off (Exp 3).
/// Not part of the original extraction proof set — added for the richer v1 capability.
/// Does not re-prove Exp 3/4 concurrency or default fingerprint-off body tolerance.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class IdempotencyFingerprintExperimentTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public IdempotencyFingerprintExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_output = output;
		_http = fixture.WithWebHostBuilder(builder =>
		{
			builder.ConfigureTestServices(services =>
			{
				services.PostConfigure<IdempotencyOptions>(o =>
				{
					o.EnableRequestFingerprint = true;
				});
			});
		}).CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Order_create_fingerprint_conflict_and_same_body_replay_are_observed()
	{
		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var key = System.Ulid.NewUlid().ToString();
		var calls = new List<FingerprintCall>();

		var miss = await SendAsync(capture, calls, "MissPopulate", key, quantity: 2);
		var sameBody = await SendAsync(capture, calls, "SameKeySameBody", key, quantity: 2);
		var differentBody = await SendAsync(capture, calls, "SameKeyDifferentBody", key, quantity: 5);

		var result = new
		{
			name = "order-create-idempotency-fingerprint-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			calls,
			observations = new
			{
				miss.HttpStatus,
				miss.OrderId,
				sameBodyReplay = sameBody.CachedResponseHeader && sameBody.HttpStatus == 200,
				sameBodyOrderId = sameBody.OrderId,
				fingerprintConflictStatus = differentBody.HttpStatus
			}
		};

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		miss.HttpStatus.Should().Be(200);
		miss.OrderId.Should().NotBeEmpty();
		miss.MediatorSpanCount.Should().BeGreaterThan(0);

		sameBody.HttpStatus.Should().Be(200);
		sameBody.OrderId.Should().Be(miss.OrderId);
		sameBody.CachedResponseHeader.Should().BeTrue();
		sameBody.MediatorSpanCount.Should().Be(0);

		differentBody.HttpStatus.Should().Be(422,
			"EnableRequestFingerprint must reject same Idempotency-Key with a different body");
		differentBody.CachedResponseHeader.Should().BeFalse();
		differentBody.MediatorSpanCount.Should().Be(0,
			"fingerprint conflict must not run production. Spans: {0}",
			Describe(capture.ForTraceHex(differentBody.TraceId)));
	}

	private async Task<FingerprintCall> SendAsync(
		InProcessActivityCapture capture,
		List<FingerprintCall> calls,
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

		var call = new FingerprintCall(
			behavior,
			idempotencyKey,
			quantity,
			result.HttpStatus,
			result.OrderId,
			result.CachedResponseHeader,
			result.Spans.Count(IsMediator),
			result.Spans.Count(IsNpgsql),
			result.TraceIdHex,
			result.ClientDurationMs,
			result.Body);
		calls.Add(call);
		return call;
	}

	private static string Describe(IReadOnlyList<CapturedActivity> spans)
		=> string.Join("; ", spans.Select(s => $"{s.Source}:{s.DisplayName}"));

	private sealed record FingerprintCall(
		string Behavior,
		string IdempotencyKey,
		int RequestedQuantity,
		int HttpStatus,
		Guid OrderId,
		bool CachedResponseHeader,
		int MediatorSpanCount,
		int NpgsqlSpanCount,
		string TraceId,
		double ClientDurationMs,
		string Body);
}
