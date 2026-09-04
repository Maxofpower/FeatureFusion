using System.Text.Json;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.EventBusLab;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace IntegrationTests.Experiments.EventBusObservationBaseline;

/// <summary>
/// Experiment 19: EventBus observation-seam baseline (happy path).
/// Hypothesis: for a cache-miss HTTP order create, the Lab journal + DB + handler
/// observation can establish the ordered lifecycle
/// outbox → publish → processor → handler, distinguishing Observed vs Inferred facts.
/// Does not inject faults. Does not add W3C propagation. Does not change production semantics
/// when the Lab hook is registered but faults are disarmed.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class EventBusObservationBaselineExperimentTests
{
	private const int Quantity = 2;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public EventBusObservationBaselineExperimentTests(AspireFixture fixture, ITestOutputHelper output)
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
	public async Task Http_order_eventbus_lifecycle_stages_are_journaled_as_observed_vs_inferred()
	{
		_fixture.ProcessedEvents.Clear();
		_fixture.EventBusJournal.Clear();
		_fixture.EventBusFaults.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var key = System.Ulid.NewUlid().ToString();

		var http = await HttpOrderCreate.PostAsync(
			_http,
			capture,
			key,
			Quantity,
			jsonOptions: JsonOptions);

		http.HttpStatus.Should().Be(200, http.Body);
		http.OrderId.Should().NotBeEmpty();

		var outboxAfterInsert = await OrderOutboxObserver.WaitUntilExistsAsync(_services, http.OrderId);
		var outboxInsertedPending = outboxAfterInsert.WorkerPending || !outboxAfterInsert.WorkerProcessed;

		await Wait.UntilAsync(
			() => _fixture.EventBusJournal.Snapshot().Any(r =>
				r.Stage == EventBusLabStages.AfterPublishBeforeOutboxMark
				&& r.OrderId == http.OrderId),
			TimeSpan.FromSeconds(20));

		var processedOutbox = await OrderOutboxObserver.WaitUntilProcessedAsync(_services, http.OrderId);

		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == http.OrderId),
			TimeSpan.FromSeconds(20));

		await Wait.UntilAsync(
			() => _fixture.EventBusJournal.ForMessage(processedOutbox.IntegrationEventId)
				.Any(r => r.Stage == EventBusLabStages.ProcessorCompleted),
			TimeSpan.FromSeconds(10));

		var delivered = _fixture.ProcessedEvents.Single(e => e.OrderId == http.OrderId);
		delivered.Id.Should().Be(processedOutbox.IntegrationEventId);

		var inbox = await QueryInboxAsync(delivered.Id);
		var journalForMessage = _fixture.EventBusJournal.ForMessage(delivered.Id);
		var processSpans = capture.All
			.Where(s => s.Source == "EventBus"
				&& s.DisplayName == "ProcessMessage"
				&& s.Tags.TryGetValue("message.id", out var mid)
				&& mid == delivered.Id.ToString())
			.ToList();

		var stages = new
		{
			outboxInserted = new
			{
				evidenceKind = "Observed",
				observed = outboxInsertedPending || outboxAfterInsert.OutboxMessageId != Guid.Empty,
				outboxMessageId = outboxAfterInsert.OutboxMessageId,
				status = outboxAfterInsert.Status,
				note = "OrderOutboxObserver row for OrderId (pending may race if worker is fast)"
			},
			publishCompleted = new
			{
				evidenceKind = "Observed",
				observed = journalForMessage.Any(r => r.Stage == EventBusLabStages.AfterPublishBeforeOutboxMark),
				note = "IEventBusLabHook after PublishDirect, before MarkProcessed"
			},
			outboxMarkedProcessed = new
			{
				evidenceKind = "Observed",
				observed = processedOutbox.WorkerProcessed,
				status = processedOutbox.Status,
				note = "OutboxWorker MarkProcessed after hook returned"
			},
			processorEntered = new
			{
				evidenceKind = "Observed",
				observed = journalForMessage.Any(r => r.Stage == EventBusLabStages.ProcessorEntered),
				note = "LabMessageProcessorDecorator enter"
			},
			processorCompleted = new
			{
				evidenceKind = "Observed",
				observed = journalForMessage.Any(r => r.Stage == EventBusLabStages.ProcessorCompleted),
				detail = journalForMessage.LastOrDefault(r => r.Stage == EventBusLabStages.ProcessorCompleted)?.Detail
			},
			handlerInvoked = new
			{
				evidenceKind = "Observed",
				observed = _fixture.ProcessedEvents.Count(e => e.Id == delivered.Id) == 1,
				note = "TestEventHandlerDecorator / ProcessedEvents"
			},
			inboxProcessed = new
			{
				evidenceKind = "Observed",
				observed = inbox.IsProcessed,
				inboxRowCount = inbox.InboxRowCount,
				status = inbox.Status
			},
			consumerAck = new
			{
				evidenceKind = "Inferred",
				inferred = processSpans.Any(s =>
					s.Tags.TryGetValue("message.status", out var st)
					&& string.Equals(st, "processed", StringComparison.Ordinal)),
				processMessageSpanCount = processSpans.Count,
				note = "EventBus sets message.status=processed then SafeAckAsync — ack itself is not separately instrumented"
			},
			dedupGate = new
			{
				evidenceKind = "Inferred",
				exercised = false,
				note = "Happy-path first delivery; inbox/processed_messages dedup characterized by Exp 7/17 — not asserted here"
			}
		};

		var result = new
		{
			name = "eventbus-observation-baseline-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			environment = "Development",
			configuration = new
			{
				path = HttpOrderCreate.Path,
				labSeam = "IEventBusLabHook + LabMessageProcessorDecorator (faults disarmed)",
				w3cPropagation = false,
				asyncPath = "HTTP order → outbox → OutBoxWorker PublishDirect → Lab hook → MarkProcessed → RabbitMQ → MessageProcessor → handler → Ack"
			},
			orderId = http.OrderId,
			integrationEventId = delivered.Id,
			stages,
			journal = journalForMessage,
			notes = new[]
			{
				"Observed = direct Lab journal or DB/handler evidence.",
				"Inferred = derived from ProcessMessage tags / lifecycle adjacency (ack).",
				"Does not prove exactly-once broker delivery or crash consistency."
			}
		};

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		http.CachedResponseHeader.Should().BeFalse();
		stages.outboxInserted.observed.Should().BeTrue();
		stages.publishCompleted.observed.Should().BeTrue(
			"Lab hook must observe AfterPublishBeforeOutboxMark for OrderId={0}", http.OrderId);
		stages.outboxMarkedProcessed.observed.Should().BeTrue();
		stages.processorEntered.observed.Should().BeTrue();
		stages.processorCompleted.observed.Should().BeTrue();
		stages.handlerInvoked.observed.Should().BeTrue();
		inbox.InboxRowCount.Should().BeGreaterThanOrEqualTo(1);
		inbox.IsProcessed.Should().BeTrue();
		stages.consumerAck.inferred.Should().BeTrue(
			"ProcessMessage message.status=processed should be present when handler succeeded");
		_fixture.EventBusJournal.Snapshot()
			.Should().NotContain(r => r.Stage == EventBusLabStages.SimulatedCrashAfterPublish);
	}

	private async Task<(int InboxRowCount, bool IsProcessed, string? Status)> QueryInboxAsync(Guid messageId)
	{
		await using var scope = _services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var rows = await db.InboxMessages.AsNoTracking().Where(m => m.Id == messageId).ToListAsync();
		var row = rows.SingleOrDefault();
		return (rows.Count, row?.IsProcessed ?? false, row?.Status.ToString());
	}
}
