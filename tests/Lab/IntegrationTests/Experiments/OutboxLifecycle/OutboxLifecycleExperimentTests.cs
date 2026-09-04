using System.Diagnostics;
using System.Text.Json;
using EventBusRabbitMQ.Domain;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.OutboxLifecycle;

/// <summary>
/// Experiment 8: HTTP order create → transactional outbox row lifecycle fingerprint.
/// Hypothesis: a cache-miss <c>POST /api/v2/Order/order</c> inserts one
/// <c>outbox_messages</c> row (<c>Status=Pending</c>, <c>ProcessedAt=null</c>);
/// the real <c>OutBoxWorker</c> publishes via <c>PublishDirect</c> then marks the row
/// processed (<c>Status=Processed</c>, <c>ProcessedAt</c>/<c>CompletedAt</c> set);
/// the consumer/handler run once. Idempotent HTTP replay does not insert another outbox row
/// or produce a second handler observation for the same order.
/// Outbox row <c>Id</c> equals <c>IntegrationEvent.Id</c> and is distinct from
/// <c>OrderCreatedIntegrationEvent.OrderId</c>.
/// <c>AspireFixture.ProcessedEvents</c> is test observation infrastructure, not production telemetry.
/// Does not claim exactly-once RabbitMQ delivery or crash consistency between publish and mark-processed.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class OutboxLifecycleExperimentTests
{
	private const int Quantity = 2;

	/// <summary>
	/// Covers several OutBoxWorker poll intervals (2s) after replay without asserting exact timing.
	/// </summary>
	private static readonly TimeSpan ReplayObservationWindow = TimeSpan.FromSeconds(5);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public OutboxLifecycleExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_services = fixture.Services;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Http_order_create_fingerprints_outbox_lifecycle_and_replay_does_not_add_rows()
	{
		_fixture.ProcessedEvents.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var baselineKey = System.Ulid.NewUlid().ToString();
		var controlKey = System.Ulid.NewUlid().ToString();
		var calls = new List<OutboxLifecycleCall>();
		var outboxObservations = new List<OutboxSnapshot>();
		var processedObservations = new List<ProcessedEventObservation>();

		var baseline = await SendAsync(
			capture,
			calls,
			behavior: "CacheMissOutboxCreation",
			idempotencyKey: baselineKey,
			quantity: Quantity);

		baseline.HttpStatus.Should().Be(200);
		baseline.OrderId.Should().NotBeEmpty();
		baseline.CachedResponseHeader.Should().BeFalse();
		baseline.MediatorSpanCount.Should().BeGreaterThan(0);
		baseline.NpgsqlSpanCount.Should().BeGreaterThan(0);

		var outboxAfterCreate = await OrderOutboxObserver.FindByOrderIdAsync(_services, baseline.OrderId);
		outboxAfterCreate.Should().ContainSingle(
			"cache miss should persist exactly one OrderCreatedIntegrationEvent outbox row for the order");

		var createdRow = outboxAfterCreate[0];
		outboxObservations.Add(ToSnapshot(createdRow, "AfterCacheMiss"));

		createdRow.EventType.Should().Be(OrderOutboxObserver.OrderCreatedEventType);
		createdRow.IntegrationEventId.Should().Be(createdRow.OutboxMessageId,
			"outbox row primary key equals IntegrationEvent.Id in payload");
		createdRow.OrderId.Should().Be(baseline.OrderId,
			"OrderId in payload is the HTTP order identity, distinct from IntegrationEvent.Id");

		var processedRow = await OrderOutboxObserver.WaitUntilProcessedAsync(_services, baseline.OrderId);
		var processedOutbox = ToSnapshot(processedRow, "AfterWorkerProcessed");
		outboxObservations.Add(processedOutbox);

		processedOutbox.WorkerProcessed.Should().BeTrue(
			"OutBoxWorker sets Status=Processed and ProcessedAt/CompletedAt after publish");
		processedOutbox.Status.Should().Be(MessageStatus.Processed.ToString());

		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == baseline.OrderId),
			TimeSpan.FromSeconds(20));

		var handlerEvents = _fixture.ProcessedEvents
			.Where(e => e.OrderId == baseline.OrderId)
			.ToList();

		handlerEvents.Should().ContainSingle();
		var delivered = handlerEvents[0];

		delivered.Id.Should().Be(processedOutbox.IntegrationEventId,
			"handler receives the same IntegrationEvent.Id stored in outbox_messages");

		var inboxAfterDelivery = await QueryInboxAsync(processedOutbox.IntegrationEventId);

		processedObservations.Add(new ProcessedEventObservation(
			Phase: "AfterWorkerAndHandler",
			ObservedUtc: DateTimeOffset.UtcNow,
			IntegrationEventId: delivered.Id,
			OrderId: delivered.OrderId,
			Total: delivered.Total,
			InboxRowCount: inboxAfterDelivery.InboxRowCount,
			InboxProcessed: inboxAfterDelivery.IsProcessed));

		var outboxCountBeforeReplay = (await OrderOutboxObserver.FindByOrderIdAsync(_services, baseline.OrderId)).Count;

		var replay = await SendAsync(
			capture,
			calls,
			behavior: "IdempotentReplay",
			idempotencyKey: baselineKey,
			quantity: Quantity);

		var replayCompletedUtc = replay.CompletedUtc;

		await Wait.UntilAsync(
			() =>
			{
				var count = _fixture.ProcessedEvents.Count(e => e.OrderId == baseline.OrderId);
				return count == 1 && DateTimeOffset.UtcNow - replayCompletedUtc >= ReplayObservationWindow;
			},
			TimeSpan.FromSeconds(20));

		var outboxAfterReplay = await OrderOutboxObserver.FindByOrderIdAsync(_services, baseline.OrderId);
		outboxObservations.Add(ToSnapshot(outboxAfterReplay[0], "AfterIdempotentReplay"));

		var control = await SendAsync(
			capture,
			calls,
			behavior: "ControlNewIdempotencyKey",
			idempotencyKey: controlKey,
			quantity: Quantity);

		control.HttpStatus.Should().Be(200);
		control.OrderId.Should().NotBe(baseline.OrderId);
		control.CachedResponseHeader.Should().BeFalse();

		var controlOutbox = await OrderOutboxObserver.FindByOrderIdAsync(_services, control.OrderId);
		controlOutbox.Should().ContainSingle(
			"a new Idempotency-Key should create a new order and its own outbox row");

		var result = new OutboxLifecycleExperimentResult(
			Name: "http-order-outbox-lifecycle-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new OutboxLifecycleConfiguration(
				HttpOrderCreate.Path,
				HttpOrderCreate.IdempotencyHeader,
				HttpOrderCreate.CachedResponseHeader,
				AsyncPath: "POST order → IdempotentAttributeFilter (miss) → CreateOrderCommandHandler → IntegrationEventService.PublishThroughEventBusAsync → outbox_messages (transactional) → OutBoxWorker (poll ProcessedAt==null) → PublishDirect → MarkMessageAsProcessedAsync → RabbitMQ → MessageProcessor → inbox_messages → OrderCreatedIntegrationEventHandler",
				WorkerOrderingNote: "OutBoxWorker calls PublishDirect before MarkMessageAsProcessedAsync (Status=Processed, ProcessedAt, CompletedAt). Worker selects pending rows by ProcessedAt==null.",
				IdentityNote: "outbox_messages.Id == IntegrationEvent.Id != OrderCreatedIntegrationEvent.OrderId",
				ProcessedEventsNote: "AspireFixture.ProcessedEvents is test-only handler observation"),
			Calls: calls,
			OutboxObservations: outboxObservations,
			ProcessedEventObservations: processedObservations,
			Observations: new OutboxLifecycleObservations(
				BaselineOrderId: baseline.OrderId,
				BaselineIntegrationEventId: processedOutbox.IntegrationEventId,
				OutboxCountAfterCreate: outboxAfterCreate.Count,
				OutboxInitiallyPending: createdRow.WorkerPending,
				OutboxAlreadyProcessedAtCreateObservation: createdRow.WorkerProcessed,
				OutboxCountBeforeReplay: outboxCountBeforeReplay,
				OutboxCountAfterReplay: outboxAfterReplay.Count,
				ProcessedEventCountForBaselineOrder: _fixture.ProcessedEvents.Count(e => e.OrderId == baseline.OrderId),
				ReplayHttpStatus: replay.HttpStatus,
				ReplaySameOrderId: replay.OrderId == baseline.OrderId,
				ReplayCachedHeader: replay.CachedResponseHeader,
				ReplayMediatorSpans: replay.MediatorSpanCount,
				ReplayNpgsqlSpans: replay.NpgsqlSpanCount,
				ControlOrderId: control.OrderId,
				ControlOutboxCount: controlOutbox.Count,
				ControlIntegrationEventId: controlOutbox[0].IntegrationEventId,
				Notes:
				[
					$"After cache miss, outbox row Status={createdRow.Status}; ProcessedAt null={createdRow.ProcessedAtUtc is null} (worker may process within poll interval).",
					$"After worker: Status={processedOutbox.Status}; ProcessedAt={processedOutbox.ProcessedAtUtc:O}; CompletedAt={processedOutbox.CompletedAtUtc:O}.",
					$"Handler IntegrationEvent.Id={delivered.Id} matches outbox_messages.Id; OrderId={delivered.OrderId}.",
					$"Inbox row for IntegrationEvent.Id: count={inboxAfterDelivery.InboxRowCount}, processed={inboxAfterDelivery.IsProcessed}.",
					"Does not assert exactly-once RabbitMQ delivery or behavior if worker crashes between publish and mark-processed."
				]));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		replay.HttpStatus.Should().Be(200);
		replay.OrderId.Should().Be(baseline.OrderId);
		replay.CachedResponseHeader.Should().BeTrue();
		replay.MediatorSpanCount.Should().Be(0);
		replay.NpgsqlSpanCount.Should().Be(0);

		outboxAfterReplay.Should().ContainSingle();
		outboxAfterReplay[0].OutboxMessageId.Should().Be(processedOutbox.OutboxMessageId);
		outboxCountBeforeReplay.Should().Be(1);
		_fixture.ProcessedEvents.Count(e => e.OrderId == baseline.OrderId).Should().Be(1);
	}

	private async Task<OutboxLifecycleCall> SendAsync(
		InProcessActivityCapture capture,
		List<OutboxLifecycleCall> calls,
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

		var call = new OutboxLifecycleCall(
			RequestNumber: calls.Count + 1,
			Behavior: behavior,
			IdempotencyKey: idempotencyKey,
			Quantity: quantity,
			HttpStatus: result.HttpStatus,
			OrderId: result.OrderId,
			QuantityReturned: result.Quantity,
			TotalAmount: result.TotalAmount,
			CachedResponseHeader: result.CachedResponseHeader,
			ClientDurationMs: result.ClientDurationMs,
			CompletedUtc: result.CompletedUtc,
			TraceId: result.TraceIdHex,
			MediatorSpanCount: result.Spans.Count(IsMediator),
			NpgsqlSpanCount: result.Spans.Count(IsNpgsql),
			Body: result.Body);

		calls.Add(call);
		return call;
	}

	private static OutboxSnapshot ToSnapshot(OrderOutboxRow row, string phase)
		=> new(
			Phase: phase,
			ObservedUtc: DateTimeOffset.UtcNow,
			OutboxMessageId: row.OutboxMessageId,
			IntegrationEventId: row.IntegrationEventId,
			OrderId: row.OrderId,
			Total: row.Total,
			EventType: row.EventType,
			Status: row.Status,
			ProcessedAtUtc: row.ProcessedAtUtc,
			CompletedAtUtc: row.CompletedAtUtc,
			RetryCount: row.RetryCount,
			WorkerPending: row.WorkerPending,
			WorkerProcessed: row.WorkerProcessed,
			CreatedAtUtc: row.CreatedAtUtc);

	private async Task<InboxObservation> QueryInboxAsync(Guid integrationEventId)
	{
		await using var scope = _services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		var inboxRows = await db.InboxMessages
			.AsNoTracking()
			.Where(m => m.Id == integrationEventId)
			.ToListAsync();

		var row = inboxRows.SingleOrDefault();

		return new InboxObservation(
			InboxRowCount: inboxRows.Count,
			InboxStatus: row?.Status.ToString(),
			IsProcessed: row?.IsProcessed ?? false,
			ProcessedAtUtc: row?.ProcessedAt);
	}

	private sealed record OutboxLifecycleCall(
		int RequestNumber,
		string Behavior,
		string IdempotencyKey,
		int Quantity,
		int HttpStatus,
		Guid OrderId,
		int? QuantityReturned,
		decimal? TotalAmount,
		bool CachedResponseHeader,
		double ClientDurationMs,
		DateTimeOffset CompletedUtc,
		string TraceId,
		int MediatorSpanCount,
		int NpgsqlSpanCount,
		string Body);

	private sealed record OutboxSnapshot(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid OutboxMessageId,
		Guid IntegrationEventId,
		Guid OrderId,
		decimal Total,
		string EventType,
		string Status,
		DateTime? ProcessedAtUtc,
		DateTime? CompletedAtUtc,
		int RetryCount,
		bool WorkerPending,
		bool WorkerProcessed,
		DateTime CreatedAtUtc);

	private sealed record InboxObservation(
		int InboxRowCount,
		string? InboxStatus,
		bool IsProcessed,
		DateTime? ProcessedAtUtc);

	private sealed record ProcessedEventObservation(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid IntegrationEventId,
		Guid OrderId,
		decimal Total,
		int InboxRowCount,
		bool InboxProcessed);

	private sealed record OutboxLifecycleConfiguration(
		string Path,
		string IdempotencyHeader,
		string CachedResponseHeader,
		string AsyncPath,
		string WorkerOrderingNote,
		string IdentityNote,
		string ProcessedEventsNote);

	private sealed record OutboxLifecycleObservations(
		Guid BaselineOrderId,
		Guid BaselineIntegrationEventId,
		int OutboxCountAfterCreate,
		bool OutboxInitiallyPending,
		bool OutboxAlreadyProcessedAtCreateObservation,
		int OutboxCountBeforeReplay,
		int OutboxCountAfterReplay,
		int ProcessedEventCountForBaselineOrder,
		int ReplayHttpStatus,
		bool ReplaySameOrderId,
		bool ReplayCachedHeader,
		int ReplayMediatorSpans,
		int ReplayNpgsqlSpans,
		Guid ControlOrderId,
		int ControlOutboxCount,
		Guid ControlIntegrationEventId,
		IReadOnlyList<string> Notes);

	private sealed record OutboxLifecycleExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		OutboxLifecycleConfiguration Configuration,
		IReadOnlyList<OutboxLifecycleCall> Calls,
		IReadOnlyList<OutboxSnapshot> OutboxObservations,
		IReadOnlyList<ProcessedEventObservation> ProcessedEventObservations,
		OutboxLifecycleObservations Observations);
}
