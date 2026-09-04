using System.Text.Json;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
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

namespace IntegrationTests.Experiments.EventBusPublishCrash;

/// <summary>
/// Experiment 20: Lab fault B — after successful PublishDirect / before outbox MarkProcessed.
/// Hypothesis: when the Lab hook simulates a crash after broker publish, the outbox row remains
/// pending and a later worker poll may publish again; with fixture
/// <c>EnableDeduplication=false</c>, inbox completion dedup may still suppress a second handler
/// for the same <c>IntegrationEvent.Id</c>. Characterizes; does not fix duplicate-publish risk.
/// No W3C propagation.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class EventBusPublishCrashExperimentTests
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

	public EventBusPublishCrashExperimentTests(AspireFixture fixture, ITestOutputHelper output)
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
	public async Task Publish_then_crash_before_outbox_mark_is_characterized()
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

		// Arm immediately after OrderId is known — before OutBoxWorker (~2s) typically publishes.
		_fixture.EventBusFaults.ArmCrashAfterPublishOnceForOrderId(http.OrderId);

		await Wait.UntilAsync(
			() => _fixture.EventBusJournal.Snapshot().Any(r =>
				r.Stage == EventBusLabStages.SimulatedCrashAfterPublish
				&& r.OrderId == http.OrderId),
			TimeSpan.FromSeconds(20));

		var crashRecord = _fixture.EventBusJournal.Snapshot()
			.Single(r => r.Stage == EventBusLabStages.SimulatedCrashAfterPublish
				&& r.OrderId == http.OrderId);
		var messageId = crashRecord.MessageId
			?? throw new InvalidOperationException("Crash journal missing MessageId");

		var outboxAfterCrash = await OrderOutboxObserver.FindByOrderIdAsync(_services, http.OrderId);
		outboxAfterCrash.Should().ContainSingle();
		var pendingAfterCrash = outboxAfterCrash[0];
		var stillPendingAfterCrash = pendingAfterCrash.WorkerPending && !pendingAfterCrash.WorkerProcessed;

		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == http.OrderId)
				|| _fixture.EventBusJournal.ForMessage(messageId)
					.Any(r => r.Stage == EventBusLabStages.ProcessorEntered),
			TimeSpan.FromSeconds(20));

		var processedOutbox = await OrderOutboxObserver.WaitUntilProcessedAsync(_services, http.OrderId);

		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == http.OrderId),
			TimeSpan.FromSeconds(20));

		var settleStart = DateTimeOffset.UtcNow;
		await Wait.UntilAsync(
			() => DateTimeOffset.UtcNow - settleStart >= TimeSpan.FromSeconds(5),
			TimeSpan.FromSeconds(10));

		var journal = _fixture.EventBusJournal.ForMessage(messageId);
		var publishAttempts = journal.Count(r => r.Stage == EventBusLabStages.AfterPublishBeforeOutboxMark);
		var crashCount = journal.Count(r => r.Stage == EventBusLabStages.SimulatedCrashAfterPublish);
		var processorEnters = journal.Count(r => r.Stage == EventBusLabStages.ProcessorEntered);
		var processorCompletes = journal.Count(r => r.Stage == EventBusLabStages.ProcessorCompleted);
		var handlerCount = _fixture.ProcessedEvents.Count(e => e.Id == messageId);
		var handlerCountByOrder = _fixture.ProcessedEvents.Count(e => e.OrderId == http.OrderId);

		var inbox = await QueryInboxAsync(messageId);
		var processSpans = capture.All.Count(s =>
			s.Source == "EventBus"
			&& s.DisplayName == "ProcessMessage"
			&& s.Tags.TryGetValue("message.id", out var mid)
			&& mid == messageId.ToString());

		var characterization = new
		{
			brokerReceivedAtLeastOnce = processorEnters >= 1 || handlerCount >= 1,
			outboxRemainedPendingAfterCrash = stillPendingAfterCrash,
			outboxEventuallyMarkedProcessed = processedOutbox.WorkerProcessed,
			publishAttemptsObserved = publishAttempts,
			republishObserved = publishAttempts >= 2,
			consumerProcessAttempts = processorEnters,
			handlerInvocationsForMessageId = handlerCount,
			handlerInvocationsForOrderId = handlerCountByOrder,
			inboxDedupLikelyPreventedDuplicateHandler =
				processorEnters >= 2 && handlerCount == 1,
			inboxProcessed = inbox.IsProcessed
		};

		var result = new
		{
			name = "eventbus-publish-then-crash-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			environment = "Development",
			configuration = new
			{
				fault = "B: AfterPublishBeforeOutboxMark → EventBusLabSimulatedCrashException (one-shot by OrderId)",
				enableDeduplication = false,
				w3cPropagation = false
			},
			orderId = http.OrderId,
			integrationEventId = messageId,
			observations = new
			{
				stillPendingAfterCrash,
				pendingStatus = pendingAfterCrash.Status,
				processedOutboxStatus = processedOutbox.Status,
				publishAttempts,
				crashCount,
				processorEnters,
				processorCompletes,
				handlerCount,
				handlerCountByOrder,
				processMessageSpans = processSpans,
				inbox
			},
			characterization,
			journal,
			observedVsInferred = new
			{
				observed = new[]
				{
					"SimulatedCrashAfterPublish journal",
					"AfterPublishBeforeOutboxMark count",
					"Outbox pending then processed",
					"ProcessorEntered/Completed",
					"ProcessedEvents handler count",
					"Inbox row state"
				},
				inferred = new[]
				{
					"inboxDedupLikelyPreventedDuplicateHandler (processor>1 && handler==1)",
					"Broker received message because PublishDirect returned before crash"
				},
				unobservable = new[]
				{
					"Exact RabbitMQ confirm bit without broker admin API",
					"In-process memory state at simulated crash (no real process kill)",
					"W3C correlation across republish (not in scope)"
				}
			},
			notes = new[]
			{
				"Does not fix duplicate-publish risk when MarkProcessed is skipped after broker send.",
				"One-shot fault: second OutBoxWorker poll publishes without crashing.",
				"Exp 19 remains the no-fault baseline."
			}
		};

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		crashCount.Should().Be(1);
		stillPendingAfterCrash.Should().BeTrue(
			"after Lab crash the outbox row must remain pending (ProcessedAt null)");
		publishAttempts.Should().BeGreaterThanOrEqualTo(1);
		processedOutbox.WorkerProcessed.Should().BeTrue(
			"worker should eventually MarkProcessed on a later poll after the one-shot fault");
		handlerCount.Should().Be(1,
			"same IntegrationEvent.Id should invoke the order handler once (inbox dedup). handlers={0}; processorEnters={1}",
			handlerCount,
			processorEnters);
		handlerCountByOrder.Should().Be(1);
		inbox.IsProcessed.Should().BeTrue();
		characterization.brokerReceivedAtLeastOnce.Should().BeTrue();
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
