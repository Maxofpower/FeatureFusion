using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EventBusRabbitMQ;
using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Infrastructure;
using EventBusRabbitMQ.Infrastructure.EventBus;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.EventBus;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit.Abstractions;

namespace IntegrationTests.Experiments.EventBusFailure;

/// <summary>
/// Experiment 9: EventBus failure outcome / retry-DLQ behavior (discovery-oriented).
/// Hypothesis: when a failing integration event is published through the real consumer,
/// what consumer, inbox, retry-header, ACK/NACK, and DLQ behavior actually occurs?
/// Does not assume <c>RetryCount=3</c> produces three handler attempts.
/// <c>FailingIntegrationEventHandler.InvocationCount</c> is test-only observation.
/// RabbitMQ queue/DLQ state, inbox persistence, and <c>EventBus</c> Activities are production evidence.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class EventBusFailureExperimentTests : IAsyncLifetime
{
	private static readonly TimeSpan StableOutcomeTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StabilityWindow = TimeSpan.FromSeconds(3);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly ITestOutputHelper _output;

	public EventBusFailureExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_ = fixture.CreateClient();
		_services = fixture.Services;
	}

	public async Task InitializeAsync() => await _fixture.ResetRabbitMQ();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Failing_integration_event_outcome_is_observed_against_permanent_failure_control()
	{
		FailingIntegrationEventHandler.ResetInvocationCount();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var calls = new List<FailurePhaseCall>();
		var observations = new List<FailureObservation>();

		var eventBusOptions = GetRequiredService<IOptions<EventBusOptions>>().Value;
		await using var channel = await CreateChannelAsync();
		var mainQueue = eventBusOptions.SubscriptionClientName;
		var dlqName = $"{mainQueue}{RabbitMQConstants.DeadLetterQueueSuffix}";

		await channel.QueuePurgeAsync(dlqName);

		var failingEvent = new FailingIntegrationEvent(Guid.NewGuid(), 110.0m);
		var publishUtc = DateTimeOffset.UtcNow;

		calls.Add(new FailurePhaseCall(
			1,
			"FailingHandlerPublish",
			failingEvent.Id,
			"FailingIntegrationEvent",
			PublishedUtc: publishUtc,
			InitialRetryHeader: 0));

		await GetRequiredService<IEventBus>().PublishDirect(failingEvent);

		var failingOutcome = await WaitForStableFailingOutcomeAsync(
			capture,
			channel,
			mainQueue,
			dlqName,
			failingEvent.Id);

		var failingInbox = await QueryInboxAsync(failingEvent.Id);
		var failingActivities = GetProcessMessageActivities(capture, failingEvent.Id);

		observations.Add(new FailureObservation(
			Phase: "AfterFailingHandler",
			ObservedUtc: DateTimeOffset.UtcNow,
			MessageId: failingEvent.Id,
			Outcome: failingOutcome.Outcome.ToString(),
			HandlerInvocationCount: FailingIntegrationEventHandler.InvocationCount,
			ConsumerProcessMessageCount: failingActivities.Count,
			ProcessMessageStatuses: failingActivities.Select(a => GetTag(a, "message.status")).Distinct().ToList(),
			ProcessMessageRetryCounts: failingActivities.Select(a => GetTag(a, "message.retry_count")).Distinct().ToList(),
			InitialPublishRetryHeader: 0,
			FinalDlqRetryHeader: failingOutcome.DlqRetryHeader,
			MainQueueMessageCount: failingOutcome.MainQueueMessageCount,
			DlqMessageCount: failingOutcome.DlqMessageCount,
			DlqContainsMessage: failingOutcome.DlqContainsMessage,
			InboxRowCount: failingInbox.InboxRowCount,
			InboxMessageStatus: failingInbox.MessageStatus,
			InboxIsProcessed: failingInbox.IsProcessed,
			InboxSubscriberName: failingInbox.SubscriberName,
			InboxSubscriberStatus: failingInbox.SubscriberStatus,
			StableObservationDurationMs: failingOutcome.StableObservationDurationMs,
			Notes: failingOutcome.Notes));

		var invalidMessageId = Guid.NewGuid();
		var invalidPublishUtc = DateTimeOffset.UtcNow;

		calls.Add(new FailurePhaseCall(
			2,
			"PermanentFailureControl",
			invalidMessageId,
			"OrderCreatedIntegrationEvent",
			PublishedUtc: invalidPublishUtc,
			InitialRetryHeader: 0));

		await channel.QueuePurgeAsync(dlqName);

		var invalidProps = new BasicProperties
		{
			MessageId = invalidMessageId.ToString(),
			Headers = new Dictionary<string, object?>
			{
				[RabbitMQConstants.EventTypeHeader] = "NonExistentEventType",
				[RabbitMQConstants.SourceServiceHeader] = "TestService",
				[RabbitMQConstants.RetryCountHeaderKey] = 0
			}
		};

		await channel.BasicPublishAsync(
			exchange: RabbitMQConstants.MainExchangeName,
			routingKey: "OrderCreatedIntegrationEvent",
			mandatory: true,
			basicProperties: invalidProps,
			body: Encoding.UTF8.GetBytes("{ invalid json }"));

		await Wait.UntilAsync(
			() => GetQueueMessageCount(channel, dlqName) >= 1,
			TimeSpan.FromSeconds(30));

		var controlDlqMessage = await GetDlqMessageByIdAsync(channel, dlqName, invalidMessageId);
		var controlInbox = await QueryInboxAsync(invalidMessageId);
		var controlActivities = GetProcessMessageActivities(capture, invalidMessageId);

		observations.Add(new FailureObservation(
			Phase: "AfterPermanentFailureControl",
			ObservedUtc: DateTimeOffset.UtcNow,
			MessageId: invalidMessageId,
			Outcome: "DeadLettered",
			HandlerInvocationCount: FailingIntegrationEventHandler.InvocationCount,
			ConsumerProcessMessageCount: controlActivities.Count,
			ProcessMessageStatuses: controlActivities.Select(a => GetTag(a, "message.status")).Distinct().ToList(),
			ProcessMessageRetryCounts: controlActivities.Select(a => GetTag(a, "message.retry_count")).Distinct().ToList(),
			InitialPublishRetryHeader: 0,
			FinalDlqRetryHeader: controlDlqMessage?.RetryHeader,
			MainQueueMessageCount: GetQueueMessageCount(channel, mainQueue),
			DlqMessageCount: GetQueueMessageCount(channel, dlqName),
			DlqContainsMessage: controlDlqMessage is not null,
			InboxRowCount: controlInbox.InboxRowCount,
			InboxMessageStatus: controlInbox.MessageStatus,
			InboxIsProcessed: controlInbox.IsProcessed,
			InboxSubscriberName: controlInbox.SubscriberName,
			InboxSubscriberStatus: controlInbox.SubscriberStatus,
			StableObservationDurationMs: null,
			Notes:
			[
				"Invalid JSON + unregistered Event-Type header; fails before handler dispatch.",
				controlDlqMessage is null
					? "DLQ message not retrieved by MessageId (unexpected)."
					: $"DLQ MessageId={controlDlqMessage.MessageId}; x-retry-count={controlDlqMessage.RetryHeader}."
			]));

		var conclusion = BuildConclusion(
			failingOutcome,
			failingInbox,
			FailingIntegrationEventHandler.InvocationCount,
			failingActivities,
			eventBusOptions);

		var result = new EventBusFailureExperimentResult(
			Name: "event-bus-failure-outcome-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new EventBusFailureConfiguration(
				MainQueue: mainQueue,
				DlqQueue: dlqName,
				EnableDeduplication: eventBusOptions.EnableDeduplication,
				ConfiguredRetryCount: eventBusOptions.RetryCount,
				IntendedRetryMechanism: "MessagetHandler: RetryLater + attemptNumber < RetryCount+1 → BasicNack(requeue:true); else BasicNack(requeue:false) → DLQ via x-dead-letter-exchange",
				ImplementationCaveats:
				[
					"x-retry-count header is published as 0 and is not incremented on requeue in consumer code",
					"inbox dedup short-circuits only when the message is successfully processed (IsProcessed / Status=Processed); pending/failed rows may redispatch (Exp 7 successful duplicate vs Exp 11 retry)",
					"CalculateRetryDelay is computed but not applied before requeue"
				],
				HandlerInvocationNote: "FailingIntegrationEventHandler.InvocationCount is test-only",
				RetryLaterPhaseOmittedReason: "FailingIntegrationEvent throws plain Exception (PermanentFailure); TransientException retry path is covered in Experiment 11"),
			Calls: calls,
			Observations: observations,
			Conclusion: conclusion);

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		failingOutcome.Outcome.Should().Be(
			FailureOutcomeKind.DeadLettered,
			"plain handler exception should propagate PermanentFailure to DLQ");

		FailingIntegrationEventHandler.InvocationCount.Should().Be(1,
			"handler should execute once before permanent failure dead-letters");

		failingOutcome.DlqContainsMessage.Should().BeTrue();
		failingInbox.InboxRowCount.Should().BeGreaterThanOrEqualTo(1,
			"handler reached path should store inbox before DispatchToHandlers");
		failingInbox.IsProcessed.Should().BeFalse(
			"permanent handler failure must not mark inbox message processed on first attempt");
		failingInbox.SubscriberStatus.Should().Be(MessageStatus.Failed.ToString());

		controlDlqMessage.Should().NotBeNull("permanent pre-handler failure should dead-letter");
		controlInbox.InboxRowCount.Should().Be(0, "invalid/unregistered message should not store inbox before handler");
	}

	private async Task<StableFailureOutcome> WaitForStableFailingOutcomeAsync(
		InProcessActivityCapture capture,
		IChannel channel,
		string mainQueue,
		string dlqName,
		Guid messageId)
	{
		var stopwatch = Stopwatch.StartNew();
		var lastProcessCount = -1;
		var stableSinceUtc = DateTimeOffset.UtcNow;
		var notes = new List<string>();

		while (stopwatch.Elapsed < StableOutcomeTimeout)
		{
			var dlqMessage = await GetDlqMessageByIdAsync(channel, dlqName, messageId, peekOnly: true);
			if (dlqMessage is not null)
			{
				return new StableFailureOutcome(
					FailureOutcomeKind.DeadLettered,
					GetQueueMessageCount(channel, mainQueue),
					GetQueueMessageCount(channel, dlqName),
					DlqContainsMessage: true,
					DlqRetryHeader: dlqMessage.RetryHeader,
					StableObservationDurationMs: stopwatch.Elapsed.TotalMilliseconds,
					Notes:
					[
						..notes,
						"Message observed in DLQ with matching MessageId.",
						$"DLQ x-retry-count={dlqMessage.RetryHeader}."
					]);
			}

			var processCount = GetProcessMessageActivities(capture, messageId).Count;
			var mainCount = GetQueueMessageCount(channel, mainQueue);

			if (processCount != lastProcessCount)
			{
				lastProcessCount = processCount;
				stableSinceUtc = DateTimeOffset.UtcNow;
				notes.Add($"ProcessMessage count changed to {processCount} at {stopwatch.Elapsed.TotalMilliseconds:F0}ms.");
			}

			if (processCount > 0 && mainCount == 0)
			{
				var stableFor = DateTimeOffset.UtcNow - stableSinceUtc;
				if (stableFor >= StabilityWindow)
				{
					var statuses = GetProcessMessageActivities(capture, messageId)
						.Select(a => GetTag(a, "message.status"))
						.ToList();

					return new StableFailureOutcome(
						FailureOutcomeKind.Acked,
						mainCount,
						GetQueueMessageCount(channel, dlqName),
						DlqContainsMessage: false,
						DlqRetryHeader: null,
						StableObservationDurationMs: stopwatch.Elapsed.TotalMilliseconds,
						Notes:
						[
							..notes,
							$"Main queue empty; {processCount} ProcessMessage span(s); statuses=[{string.Join(",", statuses)}].",
							"No matching DLQ message observed within timeout window."
						]);
				}
			}

			if (mainCount > 0 && processCount == 0 && stopwatch.Elapsed >= StabilityWindow)
			{
				return new StableFailureOutcome(
					FailureOutcomeKind.MainQueuePending,
					mainCount,
					GetQueueMessageCount(channel, dlqName),
					DlqContainsMessage: false,
					DlqRetryHeader: null,
					StableObservationDurationMs: stopwatch.Elapsed.TotalMilliseconds,
					Notes:
					[
						..notes,
						"Message remains on main queue without observed ProcessMessage activity."
					]);
			}

			await Task.Delay(100);
		}

		return new StableFailureOutcome(
			FailureOutcomeKind.Inconclusive,
			GetQueueMessageCount(channel, mainQueue),
			GetQueueMessageCount(channel, dlqName),
			DlqContainsMessage: false,
			DlqRetryHeader: null,
			StableObservationDurationMs: stopwatch.Elapsed.TotalMilliseconds,
			Notes:
			[
				..notes,
				"Timed out before stable ACK or DLQ outcome."
			]);
	}

	private static FailureConclusion BuildConclusion(
		StableFailureOutcome failingOutcome,
		InboxObservation failingInbox,
		int handlerInvocations,
		IReadOnlyList<CapturedActivity> failingActivities,
		EventBusOptions options)
	{
		var retryObserved = failingActivities.Count > 1
			|| (failingOutcome.DlqRetryHeader ?? 0) > 0
			|| failingActivities.Any(a => GetTag(a, "message.status") == "retrying");

		var lines = new List<string>
		{
			$"Configured RetryCount={options.RetryCount}; EnableDeduplication={options.EnableDeduplication}.",
			$"Failing-event outcome={failingOutcome.Outcome}; handler invocations={handlerInvocations}; consumer ProcessMessage spans={failingActivities.Count}.",
			handlerInvocations == 0 && failingOutcome.Outcome == FailureOutcomeKind.DeadLettered
				? "FailingIntegrationEvent reached DLQ with zero handler invocations — failure occurred before DispatchToHandlers (not a post-handler retry/DLQ path)."
				: handlerInvocations > 0 && failingOutcome.Outcome == FailureOutcomeKind.DeadLettered
					? "Handler threw plain Exception; DispatchToHandlers returned PermanentFailure; consumer dead-lettered after one handler attempt."
					: "See ProcessMessage statuses and inbox rows in observations.",
			failingInbox.InboxRowCount > 0
				? $"Inbox row present: message Status={failingInbox.MessageStatus}, IsProcessed={failingInbox.IsProcessed}, subscriber {failingInbox.SubscriberName} Status={failingInbox.SubscriberStatus}."
				: "No inbox row for failing message.",
			"Permanent-failure control (invalid JSON) dead-letters without inbox row.",
			retryObserved
				? "Multiple consumer attempts or retrying status observed."
				: "No retry-header progression and no multi-attempt consumer processing observed (x-retry-count remained 0)."
		};

		string? designVsActual = null;
		if (handlerInvocations == 0 && failingOutcome.Outcome == FailureOutcomeKind.DeadLettered)
		{
			designVsActual =
				"FailingIntegrationEvent does not currently reach the handler; DLQ outcome reflects PermanentFailure before handler dispatch, not the intended handler-failure retry loop.";
		}
		else if (handlerInvocations > 0 && failingOutcome.Outcome == FailureOutcomeKind.DeadLettered)
		{
			designVsActual =
				"Handler threw plain Exception; PermanentFailure propagated; consumer dead-lettered as intended.";
		}

		return new FailureConclusion(
			FailingHandlerOutcome: failingOutcome.Outcome.ToString(),
			HandlerInvocationCount: handlerInvocations,
			RetryObservedForFailingEvent: retryObserved,
			InboxStoredBeforeHandler: failingInbox.InboxRowCount > 0,
			DlqReachedForFailingEvent: failingOutcome.DlqContainsMessage,
			ApparentDesignVsActual: designVsActual,
			SummaryLines: lines);
	}

	private async Task<InboxObservation> QueryInboxAsync(Guid messageId)
	{
		await using var scope = _services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		var message = await db.InboxMessages
			.AsNoTracking()
			.FirstOrDefaultAsync(m => m.Id == messageId);

		var subscriber = await db.InboxSubscriber
			.AsNoTracking()
			.FirstOrDefaultAsync(s => s.MessageId == messageId);

		return new InboxObservation(
			InboxRowCount: message is null ? 0 : 1,
			MessageStatus: message?.Status.ToString(),
			IsProcessed: message?.IsProcessed ?? false,
			SubscriberName: subscriber?.SubscriberName,
			SubscriberStatus: subscriber?.Status.ToString());
	}

	private static IReadOnlyList<CapturedActivity> GetProcessMessageActivities(
		InProcessActivityCapture capture,
		Guid messageId)
	{
		var id = messageId.ToString();
		return capture.All
			.Where(span =>
				span.Source == "EventBus"
				&& span.DisplayName == "ProcessMessage"
				&& span.Tags.TryGetValue("message.id", out var mid)
				&& mid == id)
			.ToList();
	}

	private static string? GetTag(CapturedActivity activity, string key) =>
		activity.Tags.TryGetValue(key, out var value) ? value : null;

	private static uint GetQueueMessageCount(IChannel channel, string queueName)
	{
		var queue = channel.QueueDeclarePassiveAsync(queueName).GetAwaiter().GetResult();
		return queue.MessageCount;
	}

	private async Task<DlqMessageObservation?> GetDlqMessageByIdAsync(
		IChannel channel,
		string dlqName,
		Guid messageId,
		bool peekOnly = false)
	{
		var maxScans = 50;
		for (var i = 0; i < maxScans; i++)
		{
			var message = await channel.BasicGetAsync(dlqName, autoAck: false);
			if (message is null)
				return null;

			if (Guid.Parse(message.BasicProperties.MessageId!) == messageId)
			{
				if (!peekOnly)
					await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
				else
					await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: true);

				return new DlqMessageObservation(
					messageId,
					ReadRetryHeader(message.BasicProperties));
			}

			await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: true);
		}

		return null;
	}

	private static int ReadRetryHeader(IReadOnlyBasicProperties properties)
	{
		if (properties.Headers?.TryGetValue(RabbitMQConstants.RetryCountHeaderKey, out var value) == true)
		{
			return value switch
			{
				int count => count,
				long l => (int)l,
				byte[] bytes when bytes.Length > 0 => bytes[0],
				_ => 0
			};
		}

		return 0;
	}

	private async Task<IChannel> CreateChannelAsync() =>
		await GetRequiredService<IRabbitMQPersistentConnection>().CreateChannelAsync();

	private T GetRequiredService<T>() where T : notnull =>
		_services.GetRequiredService<T>();

	private enum FailureOutcomeKind
	{
		Acked,
		DeadLettered,
		MainQueuePending,
		Inconclusive
	}

	private sealed record StableFailureOutcome(
		FailureOutcomeKind Outcome,
		uint MainQueueMessageCount,
		uint DlqMessageCount,
		bool DlqContainsMessage,
		int? DlqRetryHeader,
		double StableObservationDurationMs,
		IReadOnlyList<string> Notes);

	private sealed record DlqMessageObservation(Guid MessageId, int RetryHeader);

	private sealed record FailurePhaseCall(
		int RequestNumber,
		string Behavior,
		Guid MessageId,
		string RoutingKey,
		DateTimeOffset PublishedUtc,
		int InitialRetryHeader);

	private sealed record InboxObservation(
		int InboxRowCount,
		string? MessageStatus,
		bool IsProcessed,
		string? SubscriberName,
		string? SubscriberStatus);

	private sealed record FailureObservation(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid MessageId,
		string Outcome,
		int HandlerInvocationCount,
		int ConsumerProcessMessageCount,
		IReadOnlyList<string?> ProcessMessageStatuses,
		IReadOnlyList<string?> ProcessMessageRetryCounts,
		int InitialPublishRetryHeader,
		int? FinalDlqRetryHeader,
		uint MainQueueMessageCount,
		uint DlqMessageCount,
		bool DlqContainsMessage,
		int InboxRowCount,
		string? InboxMessageStatus,
		bool InboxIsProcessed,
		string? InboxSubscriberName,
		string? InboxSubscriberStatus,
		double? StableObservationDurationMs,
		IReadOnlyList<string> Notes);

	private sealed record EventBusFailureConfiguration(
		string MainQueue,
		string DlqQueue,
		bool EnableDeduplication,
		int ConfiguredRetryCount,
		string IntendedRetryMechanism,
		IReadOnlyList<string> ImplementationCaveats,
		string HandlerInvocationNote,
		string RetryLaterPhaseOmittedReason);

	private sealed record FailureConclusion(
		string FailingHandlerOutcome,
		int HandlerInvocationCount,
		bool RetryObservedForFailingEvent,
		bool InboxStoredBeforeHandler,
		bool DlqReachedForFailingEvent,
		string? ApparentDesignVsActual,
		IReadOnlyList<string> SummaryLines);

	private sealed record EventBusFailureExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		EventBusFailureConfiguration Configuration,
		IReadOnlyList<FailurePhaseCall> Calls,
		IReadOnlyList<FailureObservation> Observations,
		FailureConclusion Conclusion);
}
