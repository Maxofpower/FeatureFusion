using EventBusRabbitMQ.Events;

namespace EventBusRabbitMQ.Infrastructure.Messaging;

/// <summary>
/// Optional Lab-only hook invoked by <c>OutboxWorker</c> after a successful
/// <c>PublishDirect</c> and before marking the outbox row processed.
/// Not registered in normal hosts — absence means no-op (production semantics unchanged).
/// </summary>
public interface IEventBusLabHook
{
	/// <summary>
	/// Called after the broker publish succeeds for <paramref name="messageId"/>.
	/// Throw <see cref="EventBusLabSimulatedCrashException"/> to leave the outbox row pending
	/// (simulate process crash before MarkProcessed). Any other exception uses the worker's
	/// normal failure path.
	/// </summary>
	Task OnAfterPublishBeforeOutboxMarkAsync(
		Guid messageId,
		string eventType,
		IntegrationEvent @event,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Lab-only signal: outbox worker skips MarkProcessed/MarkFailed and leaves the row pending.
/// </summary>
public sealed class EventBusLabSimulatedCrashException : Exception
{
	public EventBusLabSimulatedCrashException(Guid messageId)
		: base($"Lab simulated crash after publish for outbox message {messageId}.")
	{
		MessageId = messageId;
	}

	public Guid MessageId { get; }
}
