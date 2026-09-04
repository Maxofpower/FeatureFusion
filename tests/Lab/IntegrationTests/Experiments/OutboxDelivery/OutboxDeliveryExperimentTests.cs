using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.OutboxDelivery;

/// <summary>
/// Experiment 5: HTTP order create → transactional outbox → OutBoxWorker → RabbitMQ → handler.
/// Hypothesis: a successful cache-miss <c>POST /api/v2/Order/order</c> persists catalog/outbox
/// in one transaction; <c>OutBoxWorker</c> eventually publishes <c>OrderCreatedIntegrationEvent</c>;
/// the real consumer runs once. Replaying the same <c>Idempotency-Key</c> returns the cached HTTP
/// body and does not produce a second integration event.
/// <c>AspireFixture.ProcessedEvents</c> is test observation infrastructure (decorator on the real
/// handler), not production behavior.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class OutboxDeliveryExperimentTests
{
	private const int Quantity = 2;

	/// <summary>
	/// Covers several OutBoxWorker poll intervals (2s) without asserting exact worker timing.
	/// </summary>
	private static readonly TimeSpan ReplayObservationWindow = TimeSpan.FromSeconds(5);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public OutboxDeliveryExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Http_order_create_delivers_outbox_event_once_and_idempotent_replay_does_not_redeliver()
	{
		_fixture.ProcessedEvents.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var idempotencyKey = System.Ulid.NewUlid().ToString();
		var calls = new List<OutboxDeliveryCall>();
		var processedObservations = new List<ProcessedEventObservation>();

		var baseline = await SendAsync(
			capture,
			calls,
			behavior: "BaselineProduction",
			idempotencyKey: idempotencyKey,
			quantity: Quantity);

		baseline.HttpStatus.Should().Be(200);
		baseline.OrderId.Should().NotBeEmpty();
		baseline.CachedResponseHeader.Should().BeFalse(
			"the first request with a new Idempotency-Key should execute production");
		baseline.MediatorSpanCount.Should().BeGreaterThan(0,
			"baseline should reach ISender.Send(CreateOrderCommand). Spans: {0}",
			Describe(capture.ForTraceHex(baseline.TraceId)));
		baseline.NpgsqlSpanCount.Should().BeGreaterThan(0,
			"baseline should persist catalog/outbox via SaveChanges. Spans: {0}",
			Describe(capture.ForTraceHex(baseline.TraceId)));

		var httpCompletedUtc = baseline.CompletedUtc;
		var deliveryWaitStartedUtc = DateTimeOffset.UtcNow;

		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == baseline.OrderId),
			TimeSpan.FromSeconds(20));

		var deliveryCompletedUtc = DateTimeOffset.UtcNow;
		var matchingEvents = _fixture.ProcessedEvents
			.Where(e => e.OrderId == baseline.OrderId)
			.ToList();

		matchingEvents.Should().ContainSingle(
			"OutBoxWorker should eventually deliver exactly one OrderCreatedIntegrationEvent for the order");

		var delivered = matchingEvents[0];
		processedObservations.Add(new ProcessedEventObservation(
			Phase: "AfterBaseline",
			ObservedUtc: deliveryCompletedUtc,
			IntegrationEventId: delivered.Id,
			OrderId: delivered.OrderId,
			Total: delivered.Total,
			CreationDate: delivered.CreationDate));

		var asyncDeliveryDurationMs = (deliveryCompletedUtc - httpCompletedUtc).TotalMilliseconds;

		var replay = await SendAsync(
			capture,
			calls,
			behavior: "IdempotentReplay",
			idempotencyKey: idempotencyKey,
			quantity: Quantity);

		var replayCompletedUtc = replay.CompletedUtc;

		await Wait.UntilAsync(
			() =>
			{
				var count = _fixture.ProcessedEvents.Count(e => e.OrderId == baseline.OrderId);
				return count == 1 && DateTimeOffset.UtcNow - replayCompletedUtc >= ReplayObservationWindow;
			},
			TimeSpan.FromSeconds(20));

		var eventsAfterReplay = _fixture.ProcessedEvents
			.Where(e => e.OrderId == baseline.OrderId)
			.ToList();

		processedObservations.Add(new ProcessedEventObservation(
			Phase: "AfterReplayObservationWindow",
			ObservedUtc: DateTimeOffset.UtcNow,
			IntegrationEventId: eventsAfterReplay[0].Id,
			OrderId: eventsAfterReplay[0].OrderId,
			Total: eventsAfterReplay[0].Total,
			CreationDate: eventsAfterReplay[0].CreationDate));

		var result = new OutboxDeliveryExperimentResult(
			Name: "http-order-outbox-delivery-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new OutboxDeliveryConfiguration(
				HttpOrderCreate.Path,
				HttpOrderCreate.IdempotencyHeader,
				HttpOrderCreate.CachedResponseHeader,
				AsyncPath: "OrderController.CreateOrder → CreateOrderCommandHandler → IntegrationEventService.PublishThroughEventBusAsync → outbox_messages → OutBoxWorker → EventBus.PublishDirect → RabbitMQ → OrderCreatedIntegrationEventHandler",
				ProcessedEventsNote: "AspireFixture.ProcessedEvents is populated by TestEventHandlerDecorator wrapping the real OrderCreatedIntegrationEventHandler"),
			Calls: calls,
			ProcessedEventObservations: processedObservations,
			Observations: new OutboxDeliveryObservations(
				BaselineHttpStatus: baseline.HttpStatus,
				BaselineOrderId: baseline.OrderId,
				BaselineQuantity: baseline.Quantity,
				BaselineCachedHeader: baseline.CachedResponseHeader,
				BaselineMediatorSpans: baseline.MediatorSpanCount,
				BaselineNpgsqlSpans: baseline.NpgsqlSpanCount,
				BaselineHttpDurationMs: baseline.ClientDurationMs,
				AsyncDeliveryDurationMs: asyncDeliveryDurationMs,
				DeliveryWaitStartedUtc: deliveryWaitStartedUtc,
				DeliveryCompletedUtc: deliveryCompletedUtc,
				ProcessedEventCountAfterBaseline: matchingEvents.Count,
				ReplayHttpStatus: replay.HttpStatus,
				ReplayOrderId: replay.OrderId,
				ReplayQuantity: replay.Quantity,
				ReplayCachedHeader: replay.CachedResponseHeader,
				ReplayMediatorSpans: replay.MediatorSpanCount,
				ReplayNpgsqlSpans: replay.NpgsqlSpanCount,
				ReplayHttpDurationMs: replay.ClientDurationMs,
				ProcessedEventCountAfterReplay: eventsAfterReplay.Count,
				ReplaySameOrderIdAsBaseline: replay.OrderId == baseline.OrderId,
				Notes:
				[
					$"Baseline TraceId={baseline.TraceId}; sources={DescribeSources(capture.ForTraceHex(baseline.TraceId))}.",
					$"Replay TraceId={replay.TraceId}; sources={DescribeSources(capture.ForTraceHex(replay.TraceId))}.",
					$"ProcessedEvents after baseline: {matchingEvents.Count} event(s) for OrderId={baseline.OrderId}; IntegrationEvent.Id={delivered.Id} (distinct from OrderId).",
					$"Async delivery observed {asyncDeliveryDurationMs:F0} ms after HTTP completion; OutBoxWorker poll interval is ~2s (not asserted).",
					$"Replay observation window: {ReplayObservationWindow.TotalSeconds}s after HTTP completion before asserting no second event.",
					"ProcessedEvents is test-only observation infrastructure; it proves the real handler executed."
				]));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		replay.HttpStatus.Should().Be(200);
		replay.OrderId.Should().Be(baseline.OrderId);
		replay.Quantity.Should().Be(Quantity);
		replay.CachedResponseHeader.Should().BeTrue(
			"replay of a completed Idempotency-Key should set {0}", HttpOrderCreate.CachedResponseHeader);
		replay.MediatorSpanCount.Should().Be(0,
			"idempotent replay short-circuits before the controller action. Spans: {0}",
			Describe(capture.ForTraceHex(replay.TraceId)));
		replay.NpgsqlSpanCount.Should().Be(0,
			"idempotent replay should not re-run catalog SaveChanges/outbox insert. Spans: {0}",
			Describe(capture.ForTraceHex(replay.TraceId)));

		eventsAfterReplay.Should().ContainSingle(
			"idempotent HTTP replay must not produce a second OrderCreatedIntegrationEvent delivery");
	}

	private async Task<OutboxDeliveryCall> SendAsync(
		InProcessActivityCapture capture,
		List<OutboxDeliveryCall> calls,
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

		var call = new OutboxDeliveryCall(
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
			result.CompletedUtc,
			result.TraceIdHex,
			result.Spans.Count(IsMediator),
			result.Spans.Count(IsNpgsql),
			result.Body);

		calls.Add(call);
		return call;
	}

	private static string Describe(IReadOnlyList<CapturedActivity> spans)
		=> string.Join("; ", spans.Select(s => $"{s.Source}:{s.DisplayName}"));

	private static string DescribeSources(IReadOnlyList<CapturedActivity> spans)
		=> string.Join(",", spans.Select(s => s.Source).Distinct().OrderBy(s => s, StringComparer.Ordinal));

	private sealed record OutboxDeliveryCall(
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
		DateTimeOffset CompletedUtc,
		string TraceId,
		int MediatorSpanCount,
		int NpgsqlSpanCount,
		string Body);

	private sealed record ProcessedEventObservation(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid IntegrationEventId,
		Guid OrderId,
		decimal Total,
		DateTime CreationDate);

	private sealed record OutboxDeliveryConfiguration(
		string Path,
		string IdempotencyHeader,
		string CachedResponseHeader,
		string AsyncPath,
		string ProcessedEventsNote);

	private sealed record OutboxDeliveryObservations(
		int BaselineHttpStatus,
		Guid BaselineOrderId,
		int? BaselineQuantity,
		bool BaselineCachedHeader,
		int BaselineMediatorSpans,
		int BaselineNpgsqlSpans,
		double BaselineHttpDurationMs,
		double AsyncDeliveryDurationMs,
		DateTimeOffset DeliveryWaitStartedUtc,
		DateTimeOffset DeliveryCompletedUtc,
		int ProcessedEventCountAfterBaseline,
		int ReplayHttpStatus,
		Guid ReplayOrderId,
		int? ReplayQuantity,
		bool ReplayCachedHeader,
		int ReplayMediatorSpans,
		int ReplayNpgsqlSpans,
		double ReplayHttpDurationMs,
		int ProcessedEventCountAfterReplay,
		bool ReplaySameOrderIdAsBaseline,
		IReadOnlyList<string> Notes);

	private sealed record OutboxDeliveryExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		OutboxDeliveryConfiguration Configuration,
		IReadOnlyList<OutboxDeliveryCall> Calls,
		IReadOnlyList<ProcessedEventObservation> ProcessedEventObservations,
		OutboxDeliveryObservations Observations);
}
