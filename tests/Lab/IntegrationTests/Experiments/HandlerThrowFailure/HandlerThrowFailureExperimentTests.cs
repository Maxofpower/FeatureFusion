using System.Diagnostics;
using System.Text.Json;
using EventBusRabbitMQ;
using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Events;
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

namespace IntegrationTests.Experiments.HandlerThrowFailure;

/// <summary>
/// Experiment 11: handler-throw failure outcome (discovery-oriented).
/// Hypothesis: when a deserializable integration event reaches a registered handler that throws,
/// what <see cref="ProcessingResult"/> does <c>MessageProcessor</c> return, does
/// <c>EventBus.MessagetHandler</c> ACK/NACK/requeue/DLQ, and what inbox/telemetry evidence remains?
/// Does not increment broker <c>x-retry-count</c> or apply retry delay — those remain documented caveats.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class HandlerThrowFailureExperimentTests : IAsyncLifetime
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

	public HandlerThrowFailureExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_ = fixture.CreateClient();
		_services = fixture.Services;
	}

	public async Task InitializeAsync() => await _fixture.ResetRabbitMQ();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Handler_throw_outcome_is_observed_for_transient_business_and_plain_failures()
	{
		FailingIntegrationEventHandler.ResetInvocationCount();
		TransientThrowingIntegrationEventHandler.ResetInvocationCount();
		BusinessFailureIntegrationEventHandler.ResetInvocationCount();
		OnceTransientThenSucceedIntegrationEventHandler.Reset();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var eventBusOptions = GetRequiredService<IOptions<EventBusOptions>>().Value;
		await using var channel = await CreateChannelAsync();
		var mainQueue = eventBusOptions.SubscriptionClientName;
		var dlqName = $"{mainQueue}{RabbitMQConstants.DeadLetterQueueSuffix}";

		var phases = new List<HandlerThrowPhaseCall>();
		var observations = new List<HandlerThrowObservation>();

		await ObservePhaseAsync(
			capture,
			channel,
			mainQueue,
			dlqName,
			phases,
			observations,
			behavior: "PlainExceptionHandlerThrow",
			eventFactory: id => new FailingIntegrationEvent(id, 110.0m),
			routingKey: nameof(FailingIntegrationEvent),
			getHandlerInvocations: () => FailingIntegrationEventHandler.InvocationCount,
			expectedHandlerClassification: "PermanentFailure (plain Exception in DispatchToHandlers)",
			minHandlerInvocations: 1);

		await channel.QueuePurgeAsync(mainQueue);
		await channel.QueuePurgeAsync(dlqName);

		await ObservePhaseAsync(
			capture,
			channel,
			mainQueue,
			dlqName,
			phases,
			observations,
			behavior: "TransientExceptionHandlerThrow",
			eventFactory: id => new TransientThrowingIntegrationEvent(id, 120.0m),
			routingKey: nameof(TransientThrowingIntegrationEvent),
			getHandlerInvocations: () => TransientThrowingIntegrationEventHandler.InvocationCount,
			expectedHandlerClassification: "RetryLater until Attempts>=RetryCount then PermanentFailure / DLQ",
			minHandlerInvocations: eventBusOptions.RetryCount);

		await channel.QueuePurgeAsync(mainQueue);
		await channel.QueuePurgeAsync(dlqName);

		await ObservePhaseAsync(
			capture,
			channel,
			mainQueue,
			dlqName,
			phases,
			observations,
			behavior: "TransientThenSucceedHandlerRetry",
			eventFactory: id => new OnceTransientThenSucceedIntegrationEvent(id, 140.0m),
			routingKey: nameof(OnceTransientThenSucceedIntegrationEvent),
			getHandlerInvocations: () =>
			{
				var call = phases.Last(p => p.Behavior == "TransientThenSucceedHandlerRetry");
				return OnceTransientThenSucceedIntegrationEventHandler.InvocationCountFor(call.MessageId);
			},
			expectedHandlerClassification: "RetryLater then Success",
			minHandlerInvocations: 2);

		await channel.QueuePurgeAsync(mainQueue);
		await channel.QueuePurgeAsync(dlqName);

		await ObservePhaseAsync(
			capture,
			channel,
			mainQueue,
			dlqName,
			phases,
			observations,
			behavior: "BusinessExceptionHandlerThrow",
			eventFactory: id => new BusinessFailureIntegrationEvent(id, 130.0m),
			routingKey: nameof(BusinessFailureIntegrationEvent),
			getHandlerInvocations: () => BusinessFailureIntegrationEventHandler.InvocationCount,
			expectedHandlerClassification: "PermanentFailure (BusinessException in DispatchToHandlers)",
			minHandlerInvocations: 1);

		var result = new HandlerThrowFailureExperimentResult(
			Name: "handler-throw-failure-outcome-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new HandlerThrowFailureConfiguration(
				MainQueue: mainQueue,
				DlqQueue: dlqName,
				ConfiguredRetryCount: eventBusOptions.RetryCount,
				EnableDeduplication: eventBusOptions.EnableDeduplication,
				ConsumerPath: "PublishDirect → RabbitMQ → EventBus.MessagetHandler → MessageProcessor.ProcessMessageAsync → inbox_messages → DispatchToHandlers",
				ImplementationCaveats:
				[
					"x-retry-count is the RabbitMQ broker header and is not incremented on requeue; it is not the inbox_subscribers.Attempts budget",
					"message.retry_count ProcessMessage tag is the broker header (typically 0), not DB Attempts",
					"CalculateRetryDelay is computed on RetryLater branch but not applied before BasicNack(requeue:true)",
					"RetryCount bounds handler executions via inbox_subscribers.Attempts; exhausted RetryLater is converted to PermanentFailure before ACK/NACK"
				]),
			Phases: phases,
			Observations: observations,
			Conclusion: BuildConclusion(observations, eventBusOptions));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		var plain = observations.Single(o => o.Phase == "PlainExceptionHandlerThrow");
		var transient = observations.Single(o => o.Phase == "TransientExceptionHandlerThrow");
		var business = observations.Single(o => o.Phase == "BusinessExceptionHandlerThrow");
		var recovered = observations.Single(o => o.Phase == "TransientThenSucceedHandlerRetry");

		AssertPermanentFailurePhase(plain, expectedAttempts: 1);
		AssertBoundedTransientFailurePhase(transient, eventBusOptions.RetryCount);
		AssertPermanentFailurePhase(business, expectedAttempts: 1);
		AssertSuccessfulTransientRetryPhase(recovered);
	}

	private static void AssertPermanentFailurePhase(HandlerThrowObservation obs, int expectedAttempts)
	{
		obs.HandlerInvocationCount.Should().Be(1,
			"{0} should invoke the handler once before permanent failure", obs.Phase);
		obs.ConsumerOutcome.Should().Be(
			ConsumerOutcomeKind.DeadLettered,
			"{0}: PermanentFailure should dead-letter", obs.Phase);
		obs.ProcessMessageStatuses.Should().Contain("failed",
			"{0}: ProcessMessage should record failed status on DLQ path", obs.Phase);
		obs.DlqContainsMessage.Should().BeTrue(
			"{0}: permanent handler failure should reach DLQ", obs.Phase);
		obs.InboxIsProcessed.Should().BeFalse(
			"{0}: permanent handler failure must not mark inbox processed on first attempt", obs.Phase);
		obs.SubscriberStatus.Should().Be(MessageStatus.Failed.ToString(),
			"{0}: subscriber status should reflect handler failure", obs.Phase);
		obs.SubscriberAttempts.Should().Be(expectedAttempts,
			"{0}: permanent failure should record one handler attempt", obs.Phase);
	}

	private static void AssertBoundedTransientFailurePhase(HandlerThrowObservation obs, int configuredRetryCount)
	{
		obs.HandlerInvocationCount.Should().Be(configuredRetryCount,
			"{0}: RetryCount={1} must allow exactly {1} handler executions", obs.Phase, configuredRetryCount);
		obs.ConsumerProcessMessageCount.Should().Be(configuredRetryCount,
			"{0}: each handler execution should correspond to one ProcessMessage delivery", obs.Phase);
		obs.ConsumerOutcome.Should().Be(
			ConsumerOutcomeKind.DeadLettered,
			"{0}: exhausted RetryLater should dead-letter", obs.Phase);
		obs.ProcessMessageStatuses.Should().Contain("retrying",
			"{0}: early attempts should record retrying before requeue", obs.Phase);
		obs.ProcessMessageStatuses.Should().Contain("failed",
			"{0}: final exhausted attempt should record failed on DLQ path", obs.Phase);
		obs.DlqContainsMessage.Should().BeTrue(
			"{0}: transient failure must DLQ after RetryCount handler executions", obs.Phase);
		obs.InboxIsProcessed.Should().BeFalse(
			"{0}: exhausted transient failure must not mark inbox processed", obs.Phase);
		obs.SubscriberStatus.Should().Be(MessageStatus.Failed.ToString(),
			"{0}: subscriber status should reflect handler failure", obs.Phase);
		obs.SubscriberAttempts.Should().Be(configuredRetryCount,
			"{0}: inbox_subscribers.Attempts must equal RetryCount", obs.Phase);
		obs.ProcessMessageRetryCounts
			.Where(v => v is not null)
			.Should()
			.AllBe("0",
				"{0}: message.retry_count tag is the broker x-retry-count header (still 0), not DB Attempts", obs.Phase);
	}

	private static void AssertSuccessfulTransientRetryPhase(HandlerThrowObservation obs)
	{
		obs.HandlerInvocationCount.Should().Be(2,
			"{0}: first TransientException then success should invoke the handler twice", obs.Phase);
		obs.ConsumerOutcome.Should().Be(
			ConsumerOutcomeKind.Acked,
			"{0}: successful retry should ACK", obs.Phase);
		obs.ProcessMessageStatuses.Should().Contain("retrying");
		obs.ProcessMessageStatuses.Should().Contain("processed");
		obs.DlqContainsMessage.Should().BeFalse(
			"{0}: successful retry must not dead-letter", obs.Phase);
		obs.InboxIsProcessed.Should().BeTrue(
			"{0}: successful retry should mark inbox processed", obs.Phase);
		obs.SubscriberStatus.Should().Be(MessageStatus.Processed.ToString(),
			"{0}: subscriber should be Processed after successful retry", obs.Phase);
		obs.SubscriberAttempts.Should().Be(2,
			"{0}: Attempts should count both the failed and successful handler executions", obs.Phase);
	}

	private async Task ObservePhaseAsync(
		InProcessActivityCapture capture,
		IChannel channel,
		string mainQueue,
		string dlqName,
		List<HandlerThrowPhaseCall> phases,
		List<HandlerThrowObservation> observations,
		string behavior,
		Func<Guid, IntegrationEvent> eventFactory,
		string routingKey,
		Func<int> getHandlerInvocations,
		string expectedHandlerClassification,
		int minHandlerInvocations = 1,
		bool waitForRetryTelemetry = false)
	{
		var @event = eventFactory(Guid.NewGuid());
		var publishUtc = DateTimeOffset.UtcNow;

		phases.Add(new HandlerThrowPhaseCall(
			RequestNumber: phases.Count + 1,
			Behavior: behavior,
			MessageId: @event.Id,
			RoutingKey: routingKey,
			PublishedUtc: publishUtc,
			ExpectedHandlerClassification: expectedHandlerClassification));

		await GetRequiredService<IEventBus>().PublishDirect(@event);

		await Wait.UntilAsync(
			() => getHandlerInvocations() >= minHandlerInvocations,
			TimeSpan.FromSeconds(30));

		if (waitForRetryTelemetry)
		{
			await Wait.UntilAsync(
				() =>
				{
					var activities = GetProcessMessageActivities(capture, @event.Id);
					return activities.Any(a => GetTag(a, "message.status") == "retrying")
						|| activities.Count >= 2;
				},
				TimeSpan.FromSeconds(30));
		}

		var outcome = await WaitForStableConsumerOutcomeAsync(capture, channel, mainQueue, dlqName, @event.Id);
		var inbox = await QueryInboxAsync(@event.Id);
		var activities = GetProcessMessageActivities(capture, @event.Id);

		observations.Add(new HandlerThrowObservation(
			Phase: behavior,
			ObservedUtc: DateTimeOffset.UtcNow,
			MessageId: @event.Id,
			ExpectedHandlerClassification: expectedHandlerClassification,
			HandlerInvocationCount: getHandlerInvocations(),
			ConsumerOutcome: outcome.Outcome,
			ConsumerProcessMessageCount: activities.Count,
			ProcessMessageStatuses: activities.Select(a => GetTag(a, "message.status")).Distinct().ToList(),
			ProcessMessageRetryCounts: activities.Select(a => GetTag(a, "message.retry_count")).Distinct().ToList(),
			ProcessMessageRetryDelayMs: activities
				.Select(a => GetTag(a, "message.retry_delay_ms"))
				.Where(v => v is not null)
				.Distinct()
				.ToList(),
			MainQueueMessageCountAfterStable: outcome.MainQueueMessageCount,
			DlqMessageCountAfterStable: outcome.DlqMessageCount,
			DlqContainsMessage: outcome.DlqContainsMessage,
			InboxRowCount: inbox.InboxRowCount,
			InboxMessageStatus: inbox.MessageStatus,
			InboxIsProcessed: inbox.IsProcessed,
			SubscriberName: inbox.SubscriberName,
			SubscriberStatus: inbox.SubscriberStatus,
			SubscriberAttempts: inbox.SubscriberAttempts,
			StableObservationDurationMs: outcome.StableObservationDurationMs,
			Notes: outcome.Notes));
	}

	private async Task<StableConsumerOutcome> WaitForStableConsumerOutcomeAsync(
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
				return new StableConsumerOutcome(
					ConsumerOutcomeKind.DeadLettered,
					GetQueueMessageCount(channel, mainQueue),
					GetQueueMessageCount(channel, dlqName),
					DlqContainsMessage: true,
					StableObservationDurationMs: stopwatch.Elapsed.TotalMilliseconds,
					Notes:
					[
						..notes,
						"Message observed in DLQ.",
						$"DLQ x-retry-count={dlqMessage.RetryHeader}."
					]);
			}

			var processCount = GetProcessMessageActivities(capture, messageId).Count;
			var mainCount = GetQueueMessageCount(channel, mainQueue);

			if (processCount != lastProcessCount)
			{
				lastProcessCount = processCount;
				stableSinceUtc = DateTimeOffset.UtcNow;
				notes.Add($"ProcessMessage count={processCount} at {stopwatch.Elapsed.TotalMilliseconds:F0}ms.");
			}

			if (processCount > 0 && mainCount == 0)
			{
				if (DateTimeOffset.UtcNow - stableSinceUtc >= StabilityWindow)
				{
					var statuses = GetProcessMessageActivities(capture, messageId)
						.Select(a => GetTag(a, "message.status"))
						.ToList();
					var processed = statuses.Contains("processed");
					var failed = statuses.Contains("failed");

					return new StableConsumerOutcome(
						processed && !failed ? ConsumerOutcomeKind.Acked : ConsumerOutcomeKind.Inconclusive,
						mainCount,
						GetQueueMessageCount(channel, dlqName),
						DlqContainsMessage: false,
						StableObservationDurationMs: stopwatch.Elapsed.TotalMilliseconds,
						Notes:
						[
							..notes,
							$"Stable outcome after {stopwatch.Elapsed.TotalMilliseconds:F0}ms; ProcessMessage statuses=[{string.Join(",", statuses)}]."
						]);
				}
			}

			await Task.Delay(100);
		}

		return new StableConsumerOutcome(
			ConsumerOutcomeKind.Inconclusive,
			GetQueueMessageCount(channel, mainQueue),
			GetQueueMessageCount(channel, dlqName),
			DlqContainsMessage: false,
			StableObservationDurationMs: stopwatch.Elapsed.TotalMilliseconds,
			Notes: [..notes, "Timed out before stable consumer outcome."]);
	}

	private static HandlerThrowFailureConclusion BuildConclusion(
		IReadOnlyList<HandlerThrowObservation> observations,
		EventBusOptions options)
	{
		var permanentPhases = observations.Where(o =>
			o.Phase is "PlainExceptionHandlerThrow" or "BusinessExceptionHandlerThrow").ToList();
		var transientPhase = observations.Single(o => o.Phase == "TransientExceptionHandlerThrow");
		var recoveredPhase = observations.SingleOrDefault(o => o.Phase == "TransientThenSucceedHandlerRetry");

		var permanentDeadLettered = permanentPhases.All(o =>
			o.ConsumerOutcome == ConsumerOutcomeKind.DeadLettered && o.DlqContainsMessage);
		var transientBounded = transientPhase.HandlerInvocationCount == options.RetryCount
			&& transientPhase.DlqContainsMessage
			&& transientPhase.SubscriberAttempts == options.RetryCount;
		var recovered = recoveredPhase is { HandlerInvocationCount: 2, InboxIsProcessed: true, DlqContainsMessage: false };
		var anyDlq = observations.Any(o => o.DlqContainsMessage);
		var retryHeaderProgressed = observations.Any(o =>
			o.ProcessMessageRetryCounts.Any(v => v is not null && v != "0"));

		return new HandlerThrowFailureConclusion(
			AllPhasesAckedDespiteHandlerThrow: observations.All(o => o.ConsumerOutcome == ConsumerOutcomeKind.Acked),
			AnyRetryingStatusObserved: observations.Any(o =>
				o.ProcessMessageStatuses.Contains("retrying") || o.ConsumerOutcome == ConsumerOutcomeKind.Requeued),
			AnyDlqObserved: anyDlq,
			RetryHeaderProgressed: retryHeaderProgressed,
			RetryDelayTagObserved: observations.Any(o => o.ProcessMessageRetryDelayMs.Count > 0),
			SummaryLines:
			[
				$"Configured RetryCount={options.RetryCount}; EnableDeduplication={options.EnableDeduplication}.",
				permanentDeadLettered
					? "Plain and BusinessException handler failures dead-lettered after one handler attempt; inbox not marked processed."
					: "Permanent-failure phases did not all dead-letter — see per-phase ConsumerOutcome.",
				transientBounded
					? $"TransientException executed the handler RetryCount={options.RetryCount} times (Attempts={transientPhase.SubscriberAttempts}) then dead-lettered; ProcessMessage spans={transientPhase.ConsumerProcessMessageCount}."
					: "TransientException did not observe bounded RetryCount → DLQ.",
				recovered
					? "Once-transient-then-succeed recovered on attempt 2; inbox processed; no DLQ."
					: "Successful-retry phase was not observed or did not complete as processed.",
				retryHeaderProgressed
					? "x-retry-count / message.retry_count broker header progressed on at least one attempt."
					: "message.retry_count remained 0 (broker x-retry-count); inbox_subscribers.Attempts is the retry budget.",
				"DispatchToHandlers still aggregates RetryLater > PermanentFailure > Success; exhausted RetryLater is converted to PermanentFailure using Attempts >= RetryCount."
			]);
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
			SubscriberStatus: subscriber?.Status.ToString(),
			SubscriberAttempts: subscriber?.Attempts ?? 0);
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
		for (var i = 0; i < 50; i++)
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

				return new DlqMessageObservation(messageId, ReadRetryHeader(message.BasicProperties));
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

	private enum ConsumerOutcomeKind
	{
		Acked,
		Requeued,
		DeadLettered,
		Inconclusive
	}

	private sealed record StableConsumerOutcome(
		ConsumerOutcomeKind Outcome,
		uint MainQueueMessageCount,
		uint DlqMessageCount,
		bool DlqContainsMessage,
		double StableObservationDurationMs,
		IReadOnlyList<string> Notes);

	private sealed record DlqMessageObservation(Guid MessageId, int RetryHeader);

	private sealed record InboxObservation(
		int InboxRowCount,
		string? MessageStatus,
		bool IsProcessed,
		string? SubscriberName,
		string? SubscriberStatus,
		int SubscriberAttempts);

	private sealed record HandlerThrowPhaseCall(
		int RequestNumber,
		string Behavior,
		Guid MessageId,
		string RoutingKey,
		DateTimeOffset PublishedUtc,
		string ExpectedHandlerClassification);

	private sealed record HandlerThrowObservation(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid MessageId,
		string ExpectedHandlerClassification,
		int HandlerInvocationCount,
		ConsumerOutcomeKind ConsumerOutcome,
		int ConsumerProcessMessageCount,
		IReadOnlyList<string?> ProcessMessageStatuses,
		IReadOnlyList<string?> ProcessMessageRetryCounts,
		IReadOnlyList<string?> ProcessMessageRetryDelayMs,
		uint MainQueueMessageCountAfterStable,
		uint DlqMessageCountAfterStable,
		bool DlqContainsMessage,
		int InboxRowCount,
		string? InboxMessageStatus,
		bool InboxIsProcessed,
		string? SubscriberName,
		string? SubscriberStatus,
		int SubscriberAttempts,
		double StableObservationDurationMs,
		IReadOnlyList<string> Notes);

	private sealed record HandlerThrowFailureConfiguration(
		string MainQueue,
		string DlqQueue,
		int ConfiguredRetryCount,
		bool EnableDeduplication,
		string ConsumerPath,
		IReadOnlyList<string> ImplementationCaveats);

	private sealed record HandlerThrowFailureConclusion(
		bool AllPhasesAckedDespiteHandlerThrow,
		bool AnyRetryingStatusObserved,
		bool AnyDlqObserved,
		bool RetryHeaderProgressed,
		bool RetryDelayTagObserved,
		IReadOnlyList<string> SummaryLines);

	private sealed record HandlerThrowFailureExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		HandlerThrowFailureConfiguration Configuration,
		IReadOnlyList<HandlerThrowPhaseCall> Phases,
		IReadOnlyList<HandlerThrowObservation> Observations,
		HandlerThrowFailureConclusion Conclusion);
}
