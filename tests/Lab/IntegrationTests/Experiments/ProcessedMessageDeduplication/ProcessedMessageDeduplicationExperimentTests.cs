using System.Diagnostics;
using System.Text.Json;
using EventBusRabbitMQ;
using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Infrastructure.EventBus;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace IntegrationTests.Experiments.ProcessedMessageDeduplication;

/// <summary>
/// Experiment 17 — <c>EnableDeduplication=true</c> + duplicate message delivery.
/// Hypothesis: with the lab EventBus option enabled, the first delivery of an
/// <see cref="OrderCreatedIntegrationEvent"/> runs the handler and records
/// <c>processed_messages</c>; a second <c>PublishDirect</c> with the <b>same</b>
/// <c>IntegrationEvent.Id</c> reaches the consumer again but is suppressed before
/// handler dispatch (MessageProcessor checks <c>processed_messages</c> before inbox
/// and before <c>DispatchToHandlers</c>), yielding exactly one handler observation.
/// Contrasts Exp 7 (<c>EnableDeduplication=false</c>, inbox completion dedup only,
/// <c>processed_messages</c> unused). Test-host flips the option on the shared
/// <see cref="EventBusOptions"/> instance (same reference EventBus reads per message);
/// Lab/fixture default remains false. No production EventBus changes.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class ProcessedMessageDeduplicationExperimentTests : IAsyncLifetime
{
	private static readonly TimeSpan DuplicateObservationWindow = TimeSpan.FromSeconds(3);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly ITestOutputHelper _output;
	private bool _priorEnableDeduplication;

	public ProcessedMessageDeduplicationExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_ = fixture.CreateClient();
		_services = fixture.Services;
	}

	public async Task InitializeAsync()
	{
		await _fixture.ResetRabbitMQ();

		var options = GetRequiredService<IOptions<EventBusOptions>>().Value;
		_priorEnableDeduplication = options.EnableDeduplication;
		options.EnableDeduplication = true;
	}

	public Task DisposeAsync()
	{
		GetRequiredService<IOptions<EventBusOptions>>().Value.EnableDeduplication = _priorEnableDeduplication;
		return Task.CompletedTask;
	}

	[Fact]
	public async Task EnableDeduplication_records_processed_messages_and_suppresses_duplicate_handler_dispatch()
	{
		_fixture.ProcessedEvents.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var calls = new List<DedupCall>();
		var stateObservations = new List<DedupStateObservation>();

		var eventBusOptions = GetRequiredService<IOptions<EventBusOptions>>().Value;
		eventBusOptions.EnableDeduplication.Should().BeTrue(
			"Exp 17 must run with EnableDeduplication=true on the live EventBusOptions instance");

		var sharedOrderId = Guid.NewGuid();
		const decimal total = 91.50m;
		var baselineEvent = new OrderCreatedIntegrationEvent(sharedOrderId, total);

		var baseline = await PublishAndObserveAsync(
			capture,
			calls,
			behavior: "BaselineDelivery",
			@event: baselineEvent);

		baseline.ProcessedEventCountForMessageId.Should().Be(1,
			"baseline must execute the handler once for IntegrationEvent.Id={0}", baselineEvent.Id);

		await WaitForProcessedLayersAsync(baselineEvent.Id);

		var afterBaseline = await QueryStateAsync(baselineEvent.Id);
		stateObservations.Add(afterBaseline with { Phase = "AfterBaseline" });

		afterBaseline.InboxRowCount.Should().Be(1);
		afterBaseline.IsProcessed.Should().BeTrue();
		afterBaseline.ProcessedMessageRowCount.Should().Be(1,
			"EnableDeduplication=true must record processed_messages after successful handler completion");

		var processAttemptsAfterBaseline = CountEventBusProcessAttempts(capture, baselineEvent.Id);

		var duplicate = await PublishAndObserveAsync(
			capture,
			calls,
			behavior: "DuplicateSameIdentity",
			@event: baselineEvent,
			priorProcessedCount: baseline.ProcessedEventCountForMessageId,
			waitForObservationWindow: true);

		var afterDuplicate = await QueryStateAsync(baselineEvent.Id);
		stateObservations.Add(afterDuplicate with { Phase = "AfterDuplicate" });

		var processAttemptsAfterDuplicate = CountEventBusProcessAttempts(capture, baselineEvent.Id);
		var processStatuses = GetProcessMessageStatuses(capture, baselineEvent.Id);

		var controlEvent = new OrderCreatedIntegrationEvent(sharedOrderId, total);
		var control = await PublishAndObserveAsync(
			capture,
			calls,
			behavior: "ControlDifferentIdentity",
			@event: controlEvent);

		control.ProcessedEventCountForMessageId.Should().Be(1,
			"a new IntegrationEvent.Id must run the handler again even with EnableDeduplication=true");

		await WaitForProcessedLayersAsync(controlEvent.Id);
		var afterControl = await QueryStateAsync(controlEvent.Id);
		stateObservations.Add(afterControl with { Phase = "AfterControl" });

		var handlerCountBaselineId = _fixture.ProcessedEvents.Count(e => e.Id == baselineEvent.Id);
		var handlerCountControlId = _fixture.ProcessedEvents.Count(e => e.Id == controlEvent.Id);
		var handlerCountSharedOrder = _fixture.ProcessedEvents.Count(e => e.OrderId == sharedOrderId);

		var hypothesisConfirmed =
			handlerCountBaselineId == 1
			&& duplicate.ProcessedEventCountDeltaFromPrior == 0
			&& processAttemptsAfterDuplicate >= processAttemptsAfterBaseline + 1
			&& afterBaseline.ProcessedMessageRowCount == 1
			&& afterDuplicate.ProcessedMessageRowCount == 1
			&& afterDuplicate.InboxRowCount == 1
			&& afterControl.ProcessedMessageRowCount == 1
			&& handlerCountControlId == 1;

		var result = new
		{
			name = "event-bus-enable-deduplication-processed-messages-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			environment = "Development",
			configuration = new
			{
				consumerPath = "IEventBus.PublishDirect → RabbitMQ → EventBus.MessagetHandler → MessageProcessor.ProcessMessageAsync(deduplication:true) → OrderCreatedIntegrationEventHandler",
				enableDeduplication = eventBusOptions.EnableDeduplication,
				subscriptionClientName = eventBusOptions.SubscriptionClientName,
				optionMechanism = "Mutate shared EventBusOptions.EnableDeduplication for this test only (same instance EventBus reads per delivery); restore in DisposeAsync. Avoids a second competing WithWebHostBuilder consumer.",
				guaranteedOrdering = new[]
				{
					"1. if EnableDeduplication && processed_messages hit → ProcessingResult.Success (no DispatchToHandlers)",
					"2. else if inbox IsDuplicate (already processed) → Success (no dispatch)",
					"3. else store inbox → DispatchToHandlers → on Success Mark processed_messages then Mark inbox processed → return Success → ACK"
				},
				contrastWithExp7 = "Exp 7: EnableDeduplication=false → processed_messages unused; duplicate suppressed by inbox IsProcessed only."
			},
			calls,
			stateObservations,
			observations = new
			{
				baselineIntegrationEventId = baselineEvent.Id,
				controlIntegrationEventId = controlEvent.Id,
				sharedOrderId,
				handlerCountBaselineId,
				handlerCountControlId,
				handlerCountSharedOrder,
				processAttemptsAfterBaseline,
				processAttemptsAfterDuplicate,
				processMessageStatusesForBaselineId = processStatuses,
				duplicateIncreasedHandlerCount = duplicate.ProcessedEventCountDeltaFromPrior > 0,
				processedMessagesAfterBaseline = afterBaseline.ProcessedMessageRowCount,
				processedMessagesAfterDuplicate = afterDuplicate.ProcessedMessageRowCount,
				inboxRowsAfterDuplicate = afterDuplicate.InboxRowCount,
				hypothesisConfirmed,
				hypothesis = "EnableDeduplication=true records processed_messages on first success and suppresses a same-Id second delivery before handler dispatch."
			},
			notes = new[]
			{
				"First delivery: processed_messages miss → inbox store → handler → Mark processed_messages → Mark inbox → ACK.",
				"Second delivery (same IntegrationEvent.Id): consumer runs again (ProcessMessage), but MessageProcessor returns Success from the processed_messages check before DispatchToHandlers.",
				"After a successful first delivery, inbox IsProcessed is also true; the code checks processed_messages first, so that layer is the guaranteed early exit when EnableDeduplication=true.",
				"Control with a new IntegrationEvent.Id proves suppression is identity-based, not OrderId-based.",
				"Handler business effect remains the test decorator + in-memory ReceivedEvents (same as Exp 7); no HTTP/outbox order create in this experiment.",
				hypothesisConfirmed
					? "Hypothesis CONFIRMED."
					: "Hypothesis FALSIFIED or inconclusive relative to packaged EnableDeduplication semantics."
			}
		};

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		handlerCountBaselineId.Should().Be(1,
			"duplicate same-Id delivery must not dispatch the handler a second time");
		duplicate.ProcessedEventCountDeltaFromPrior.Should().Be(0);
		processAttemptsAfterDuplicate.Should().BeGreaterThanOrEqualTo(
			processAttemptsAfterBaseline + 1,
			"duplicate PublishDirect must still reach EventBus ProcessMessage even when suppressed");
		afterDuplicate.ProcessedMessageRowCount.Should().Be(1);
		afterDuplicate.InboxRowCount.Should().Be(1);
		afterDuplicate.IsProcessed.Should().BeTrue();

		handlerCountControlId.Should().Be(1);
		handlerCountSharedOrder.Should().Be(2,
			"baseline + control identities → two handler observations for the same OrderId");
		afterControl.ProcessedMessageRowCount.Should().Be(1,
			"control identity must also record its own processed_messages row");

		foreach (var status in processStatuses)
		{
			(status == "processed" || status == null).Should().BeTrue(
				"successful and early-Success duplicate paths ACK with message.status=processed (or unset). Statuses: [{0}]",
				string.Join(",", processStatuses));
		}
	}

	private async Task<DedupCall> PublishAndObserveAsync(
		InProcessActivityCapture capture,
		List<DedupCall> calls,
		string behavior,
		OrderCreatedIntegrationEvent @event,
		int priorProcessedCount = 0,
		bool waitForObservationWindow = false)
	{
		var eventBus = GetRequiredService<IEventBus>();
		var publishedUtc = DateTimeOffset.UtcNow;
		var processAttemptsBefore = CountEventBusProcessAttempts(capture, @event.Id);

		await eventBus.PublishDirect(@event);

		if (waitForObservationWindow)
		{
			await Wait.UntilAsync(
				() => DateTimeOffset.UtcNow - publishedUtc >= DuplicateObservationWindow,
				TimeSpan.FromSeconds(10));
		}
		else
		{
			await Wait.UntilAsync(
				() => _fixture.ProcessedEvents.Count(e => e.Id == @event.Id) > priorProcessedCount,
				TimeSpan.FromSeconds(20));
		}

		var completedUtc = DateTimeOffset.UtcNow;
		var processedForMessageId = _fixture.ProcessedEvents.Count(e => e.Id == @event.Id);
		var processAttemptsAfter = CountEventBusProcessAttempts(capture, @event.Id);

		var call = new DedupCall(
			RequestNumber: calls.Count + 1,
			Behavior: behavior,
			IntegrationEventId: @event.Id,
			OrderId: @event.OrderId,
			Total: @event.Total,
			PublishedUtc: publishedUtc,
			CompletedUtc: completedUtc,
			DurationMs: (completedUtc - publishedUtc).TotalMilliseconds,
			ProcessedEventCountForMessageId: processedForMessageId,
			ProcessedEventCountDeltaFromPrior: processedForMessageId - priorProcessedCount,
			EventBusProcessAttemptsBefore: processAttemptsBefore,
			EventBusProcessAttemptsAfter: processAttemptsAfter,
			EventBusProcessAttemptsDelta: processAttemptsAfter - processAttemptsBefore);

		calls.Add(call);
		return call;
	}

	private async Task WaitForProcessedLayersAsync(Guid messageId)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(20))
		{
			var state = await QueryStateAsync(messageId);
			if (state.InboxRowCount == 1
				&& state.IsProcessed
				&& state.InboxStatus == MessageStatus.Processed.ToString()
				&& state.ProcessedMessageRowCount == 1)
			{
				return;
			}

			await Task.Delay(100);
		}

		throw new TimeoutException(
			$"Message {messageId} was not fully recorded in inbox (processed) + processed_messages within timeout");
	}

	private async Task<DedupStateObservation> QueryStateAsync(Guid messageId)
	{
		await using var scope = _services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		var inboxRows = await db.InboxMessages
			.AsNoTracking()
			.Where(m => m.Id == messageId)
			.ToListAsync();

		var processedRows = await db.ProcessedMessages
			.AsNoTracking()
			.Where(m => m.Id == messageId)
			.ToListAsync();

		var row = inboxRows.SingleOrDefault();
		var processed = processedRows.SingleOrDefault();

		return new DedupStateObservation(
			Phase: string.Empty,
			ObservedUtc: DateTimeOffset.UtcNow,
			MessageId: messageId,
			InboxRowCount: inboxRows.Count,
			InboxStatus: row?.Status.ToString(),
			IsProcessed: row?.IsProcessed ?? false,
			InboxProcessedAtUtc: row?.ProcessedAt,
			ProcessedMessageRowCount: processedRows.Count,
			ProcessedMessageProcessedAtUtc: processed?.ProcessedAt);
	}

	private static int CountEventBusProcessAttempts(InProcessActivityCapture capture, Guid messageId)
	{
		var id = messageId.ToString();
		return capture.All.Count(span =>
			span.Source == "EventBus"
			&& span.DisplayName == "ProcessMessage"
			&& span.Tags.TryGetValue("message.id", out var mid)
			&& mid == id);
	}

	private static IReadOnlyList<string?> GetProcessMessageStatuses(InProcessActivityCapture capture, Guid messageId)
	{
		var id = messageId.ToString();
		return capture.All
			.Where(span =>
				span.Source == "EventBus"
				&& span.DisplayName == "ProcessMessage"
				&& span.Tags.TryGetValue("message.id", out var mid)
				&& mid == id)
			.Select(span => span.Tags.TryGetValue("message.status", out var status) ? status : null)
			.ToList();
	}

	private T GetRequiredService<T>() where T : notnull =>
		_services.GetRequiredService<T>();

	private sealed record DedupCall(
		int RequestNumber,
		string Behavior,
		Guid IntegrationEventId,
		Guid OrderId,
		decimal Total,
		DateTimeOffset PublishedUtc,
		DateTimeOffset CompletedUtc,
		double DurationMs,
		int ProcessedEventCountForMessageId,
		int ProcessedEventCountDeltaFromPrior,
		int EventBusProcessAttemptsBefore,
		int EventBusProcessAttemptsAfter,
		int EventBusProcessAttemptsDelta);

	private sealed record DedupStateObservation(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid MessageId,
		int InboxRowCount,
		string? InboxStatus,
		bool IsProcessed,
		DateTime? InboxProcessedAtUtc,
		int ProcessedMessageRowCount,
		DateTime? ProcessedMessageProcessedAtUtc);
}
