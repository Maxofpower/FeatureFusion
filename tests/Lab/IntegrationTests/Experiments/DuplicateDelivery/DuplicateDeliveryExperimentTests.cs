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

namespace IntegrationTests.Experiments.DuplicateDelivery;

/// <summary>
/// Experiment 7: duplicate integration-event delivery / consumer deduplication.
/// Hypothesis: when the same <see cref="OrderCreatedIntegrationEvent"/> identity
/// (<c>IntegrationEvent.Id</c> / RabbitMQ <c>MessageId</c>) is published twice through
/// the real consumer path, the inbox layer suppresses a second handler execution even though
/// <c>EventBusOptions.EnableDeduplication</c> is false in the lab fixture.
/// A second event with a new identity but the same <c>OrderId</c> payload is processed independently.
/// <see cref="AspireFixture.ProcessedEvents"/> is test observation infrastructure (decorator on the
/// real handler); <c>inbox_messages</c> rows are production persistence written by
/// <c>MessageProcessor</c>.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class DuplicateDeliveryExperimentTests : IAsyncLifetime
{
	private static readonly TimeSpan DuplicateObservationWindow = TimeSpan.FromSeconds(3);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly ITestOutputHelper _output;

	public DuplicateDeliveryExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_ = fixture.CreateClient();
		_services = fixture.Services;
	}

	public async Task InitializeAsync() => await _fixture.ResetRabbitMQ();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Duplicate_integration_event_delivery_is_suppressed_by_inbox_not_handler_replay()
	{
		_fixture.ProcessedEvents.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var calls = new List<DuplicateDeliveryCall>();
		var inboxObservations = new List<InboxObservation>();

		var eventBusOptions = GetRequiredService<IOptions<EventBusOptions>>().Value;
		var sharedOrderId = Guid.NewGuid();
		const decimal total = 88.25m;

		var baselineEvent = new OrderCreatedIntegrationEvent(sharedOrderId, total);

		var baseline = await PublishAndObserveAsync(
			capture,
			calls,
			behavior: "BaselineDelivery",
			@event: baselineEvent);

		baseline.ProcessedEventCountForMessageId.Should().Be(1,
			"baseline should execute the real handler once for IntegrationEvent.Id={0}", baselineEvent.Id);

		await WaitForInboxProcessedAsync(baselineEvent.Id);

		var inboxAfterBaseline = await QueryInboxAsync(baselineEvent.Id);
		inboxObservations.Add(inboxAfterBaseline with { Phase = "AfterBaseline" });

		inboxAfterBaseline.InboxRowCount.Should().Be(1);
		inboxAfterBaseline.IsProcessed.Should().BeTrue();
		inboxAfterBaseline.ProcessedMessageRowCount.Should().Be(0,
			"MessageDeduplicationService / processed_messages is inactive when EnableDeduplication=false");

		var duplicate = await PublishAndObserveAsync(
			capture,
			calls,
			behavior: "DuplicateDelivery",
			@event: baselineEvent,
			priorProcessedCount: baseline.ProcessedEventCountForMessageId,
			waitForObservationWindow: true);

		var inboxAfterDuplicate = await QueryInboxAsync(baselineEvent.Id);
		inboxObservations.Add(inboxAfterDuplicate with { Phase = "AfterDuplicate" });

		var controlEvent = new OrderCreatedIntegrationEvent(sharedOrderId, total);

		var control = await PublishAndObserveAsync(
			capture,
			calls,
			behavior: "ControlDifferentIdentity",
			@event: controlEvent);

		control.ProcessedEventCountForMessageId.Should().Be(1,
			"a new IntegrationEvent.Id should run the handler again even when OrderId matches baseline");

		await WaitForInboxProcessedAsync(controlEvent.Id);

		var inboxAfterControl = await QueryInboxAsync(controlEvent.Id);
		inboxObservations.Add(inboxAfterControl with { Phase = "AfterControl" });

		var processedForBaselineId = _fixture.ProcessedEvents.Count(e => e.Id == baselineEvent.Id);
		var processedForControlId = _fixture.ProcessedEvents.Count(e => e.Id == controlEvent.Id);
		var processedForSharedOrderId = _fixture.ProcessedEvents.Count(e => e.OrderId == sharedOrderId);

		var eventBusAttemptsBaselineId = CountEventBusProcessAttempts(capture, baselineEvent.Id);
		var eventBusAttemptsControlId = CountEventBusProcessAttempts(capture, controlEvent.Id);

		var result = new DuplicateDeliveryExperimentResult(
			Name: "duplicate-integration-event-delivery-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new DuplicateDeliveryConfiguration(
				ConsumerPath: "IEventBus.PublishDirect → RabbitMQ (feature_fusion queue) → EventBus.MessagetHandler → MessageProcessor.ProcessMessageAsync → inbox_messages → OrderCreatedIntegrationEventHandler (via TestEventHandlerDecorator)",
				EnableDeduplication: eventBusOptions.EnableDeduplication,
				SubscriptionClientName: eventBusOptions.SubscriptionClientName,
				DeduplicationNote: "inbox.IsDuplicateAsync(messageId) runs regardless of EnableDeduplication; MessageDeduplicationService / processed_messages runs only when EnableDeduplication=true",
				ProcessedEventsNote: "AspireFixture.ProcessedEvents records handler invocations via TestEventHandlerDecorator; OrderCreatedIntegrationEventHandler business effect is an in-memory ReceivedEvents list only"),
			Calls: calls,
			InboxObservations: inboxObservations,
			Observations: new DuplicateDeliveryObservations(
				BaselineIntegrationEventId: baselineEvent.Id,
				ControlIntegrationEventId: controlEvent.Id,
				SharedOrderId: sharedOrderId,
				SharedTotal: total,
				ProcessedCountForBaselineId: processedForBaselineId,
				ProcessedCountForControlId: processedForControlId,
				ProcessedCountForSharedOrderId: processedForSharedOrderId,
				EventBusProcessAttemptsForBaselineId: eventBusAttemptsBaselineId,
				EventBusProcessAttemptsForControlId: eventBusAttemptsControlId,
				DuplicateDeliveryIncreasedHandlerCount: duplicate.ProcessedEventCountForMessageId > baseline.ProcessedEventCountForMessageId,
				InboxRowCountAfterDuplicateForBaselineId: inboxAfterDuplicate.InboxRowCount,
				Notes:
				[
					$"EnableDeduplication={eventBusOptions.EnableDeduplication} in AspireFixture (matches lab appsettings).",
					$"Baseline IntegrationEvent.Id={baselineEvent.Id}; control IntegrationEvent.Id={controlEvent.Id}; both OrderId={sharedOrderId}.",
					$"After duplicate delivery: EventBus ProcessMessage attempts for baseline Id={eventBusAttemptsBaselineId}; ProcessedEvents for that Id={processedForBaselineId}.",
					"Second delivery with the same message identity is ACKed as Success by MessageProcessor before DispatchToHandlers when inbox_messages is already processed (IsProcessed=true).",
					"Control proves deduplication is identity-based (IntegrationEvent.Id / MessageId), not payload-based (OrderId/Total)."
				]));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		duplicate.ProcessedEventCountForMessageId.Should().Be(1,
			"duplicate delivery with the same IntegrationEvent.Id must not invoke the handler a second time");
		eventBusAttemptsBaselineId.Should().BeGreaterThanOrEqualTo(2,
			"duplicate delivery should still reach the real consumer (EventBus ProcessMessage) even when suppressed");
		inboxAfterDuplicate.InboxRowCount.Should().Be(1,
			"inbox_messages should retain a single row for the baseline message identity");
		processedForSharedOrderId.Should().Be(2,
			"baseline and control identities should each produce one handler observation for the same OrderId");
		processedForBaselineId.Should().Be(1);
		processedForControlId.Should().Be(1);
		inboxAfterControl.InboxRowCount.Should().Be(1);
		inboxAfterControl.IsProcessed.Should().BeTrue();
	}

	private async Task<DuplicateDeliveryCall> PublishAndObserveAsync(
		InProcessActivityCapture capture,
		List<DuplicateDeliveryCall> calls,
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
		var matchingProcessed = _fixture.ProcessedEvents
			.Where(e => e.Id == @event.Id)
			.ToList();

		var call = new DuplicateDeliveryCall(
			RequestNumber: calls.Count + 1,
			Behavior: behavior,
			IntegrationEventId: @event.Id,
			OrderId: @event.OrderId,
			Total: @event.Total,
			CreationDate: @event.CreationDate,
			PublishedUtc: publishedUtc,
			CompletedUtc: completedUtc,
			DurationMs: (completedUtc - publishedUtc).TotalMilliseconds,
			ProcessedEventCountForMessageId: processedForMessageId,
			ProcessedEventCountDeltaFromPrior: processedForMessageId - priorProcessedCount,
			EventBusProcessAttemptsBefore: processAttemptsBefore,
			EventBusProcessAttemptsAfter: processAttemptsAfter,
			EventBusProcessAttemptsDelta: processAttemptsAfter - processAttemptsBefore,
			LatestProcessedOrderId: matchingProcessed.LastOrDefault()?.OrderId,
			LatestProcessedTotal: matchingProcessed.LastOrDefault()?.Total);

		calls.Add(call);
		return call;
	}

	private async Task WaitForInboxProcessedAsync(Guid messageId)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(20))
		{
			var observation = await QueryInboxAsync(messageId);
			if (observation.InboxRowCount == 1
				&& observation.IsProcessed
				&& observation.InboxStatus == MessageStatus.Processed.ToString())
			{
				return;
			}

			await Task.Delay(100);
		}

		throw new TimeoutException($"Inbox message {messageId} was not marked processed within timeout");
	}

	private async Task<InboxObservation> QueryInboxAsync(Guid messageId)
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

		return new InboxObservation(
			Phase: string.Empty,
			ObservedUtc: DateTimeOffset.UtcNow,
			MessageId: messageId,
			InboxRowCount: inboxRows.Count,
			InboxStatus: row?.Status.ToString(),
			IsProcessed: row?.IsProcessed ?? false,
			InboxProcessedAtUtc: row?.ProcessedAt,
			ProcessedMessageRowCount: processedRows.Count);
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

	private T GetRequiredService<T>() where T : notnull =>
		_services.GetRequiredService<T>();

	private sealed record DuplicateDeliveryCall(
		int RequestNumber,
		string Behavior,
		Guid IntegrationEventId,
		Guid OrderId,
		decimal Total,
		DateTime CreationDate,
		DateTimeOffset PublishedUtc,
		DateTimeOffset CompletedUtc,
		double DurationMs,
		int ProcessedEventCountForMessageId,
		int ProcessedEventCountDeltaFromPrior,
		int EventBusProcessAttemptsBefore,
		int EventBusProcessAttemptsAfter,
		int EventBusProcessAttemptsDelta,
		Guid? LatestProcessedOrderId,
		decimal? LatestProcessedTotal);

	private sealed record InboxObservation(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid MessageId,
		int InboxRowCount,
		string? InboxStatus,
		bool IsProcessed,
		DateTime? InboxProcessedAtUtc,
		int ProcessedMessageRowCount);

	private sealed record DuplicateDeliveryConfiguration(
		string ConsumerPath,
		bool EnableDeduplication,
		string SubscriptionClientName,
		string DeduplicationNote,
		string ProcessedEventsNote);

	private sealed record DuplicateDeliveryObservations(
		Guid BaselineIntegrationEventId,
		Guid ControlIntegrationEventId,
		Guid SharedOrderId,
		decimal SharedTotal,
		int ProcessedCountForBaselineId,
		int ProcessedCountForControlId,
		int ProcessedCountForSharedOrderId,
		int EventBusProcessAttemptsForBaselineId,
		int EventBusProcessAttemptsForControlId,
		bool DuplicateDeliveryIncreasedHandlerCount,
		int InboxRowCountAfterDuplicateForBaselineId,
		IReadOnlyList<string> Notes);

	private sealed record DuplicateDeliveryExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		DuplicateDeliveryConfiguration Configuration,
		IReadOnlyList<DuplicateDeliveryCall> Calls,
		IReadOnlyList<InboxObservation> InboxObservations,
		DuplicateDeliveryObservations Observations);
}
