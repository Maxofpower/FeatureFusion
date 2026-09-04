using EventBusRabbitMQ.Events;
using EventBusRabbitMQ.Infrastructure.Messaging;
using FeatureFusion.Features.Order.IntegrationEvents.Events;

namespace IntegrationTests.Infrastructure.EventBusLab;

/// <summary>
/// Lab <see cref="IEventBusLabHook"/>: journals AfterPublish; optional one-shot crash at point B.
/// </summary>
public sealed class EventBusLabHook : IEventBusLabHook
{
	private readonly EventBusLabJournal _journal;
	private readonly EventBusLabFaultController _faults;

	public EventBusLabHook(EventBusLabJournal journal, EventBusLabFaultController faults)
	{
		_journal = journal;
		_faults = faults;
	}

	public Task OnAfterPublishBeforeOutboxMarkAsync(
		Guid messageId,
		string eventType,
		IntegrationEvent @event,
		CancellationToken cancellationToken = default)
	{
		Guid? orderId = @event is OrderCreatedIntegrationEvent order ? order.OrderId : null;

		_journal.Record(
			EventBusLabStages.AfterPublishBeforeOutboxMark,
			messageId,
			eventType,
			evidenceKind: "Observed",
			detail: "PublishDirect returned; outbox MarkProcessed not yet called",
			orderId: orderId);

		if (_faults.ShouldSimulateCrashAfterPublish(messageId, eventType, orderId))
		{
			_journal.Record(
				EventBusLabStages.SimulatedCrashAfterPublish,
				messageId,
				eventType,
				evidenceKind: "Observed",
				detail: "Lab fault B: skip MarkProcessed (pending outbox)",
				orderId: orderId);
			throw new EventBusLabSimulatedCrashException(messageId);
		}

		return Task.CompletedTask;
	}
}

/// <summary>Deterministic Lab fault switches. Default: all off.</summary>
public sealed class EventBusLabFaultController
{
	private Guid? _crashOnceForMessageId;
	private Guid? _crashOnceForOrderId;
	private string? _crashOnceForEventType;
	private int _crashArmedCount;

	public void Clear()
	{
		_crashOnceForMessageId = null;
		_crashOnceForOrderId = null;
		_crashOnceForEventType = null;
		_crashArmedCount = 0;
	}

	/// <summary>Arm point-B crash for the next matching outbox publish (by message id).</summary>
	public void ArmCrashAfterPublishOnce(Guid messageId)
	{
		Clear();
		_crashOnceForMessageId = messageId;
		_crashArmedCount = 1;
	}

	/// <summary>Arm point-B crash for the first publish whose payload OrderId matches.</summary>
	public void ArmCrashAfterPublishOnceForOrderId(Guid orderId)
	{
		Clear();
		_crashOnceForOrderId = orderId;
		_crashArmedCount = 1;
	}

	/// <summary>
	/// Arm point-B crash for the next publish of <paramref name="eventType"/>
	/// (e.g. <c>OrderCreatedIntegrationEvent</c>). Safe to call before OrderId is known.
	/// </summary>
	public void ArmCrashAfterPublishOnceForEventType(string eventType)
	{
		Clear();
		_crashOnceForEventType = eventType;
		_crashArmedCount = 1;
	}

	internal bool ShouldSimulateCrashAfterPublish(Guid messageId, string eventType, Guid? orderId)
	{
		if (_crashArmedCount <= 0)
			return false;

		var match = (_crashOnceForMessageId is { } mid && mid == messageId)
			|| (_crashOnceForOrderId is { } oid && orderId == oid)
			|| (_crashOnceForEventType is { } et
				&& string.Equals(et, eventType, StringComparison.Ordinal));

		if (!match)
			return false;

		_crashArmedCount = 0;
		_crashOnceForMessageId = null;
		_crashOnceForOrderId = null;
		_crashOnceForEventType = null;
		return true;
	}
}
