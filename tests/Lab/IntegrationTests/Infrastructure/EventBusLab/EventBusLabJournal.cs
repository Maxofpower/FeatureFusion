using System.Collections.Concurrent;

namespace IntegrationTests.Infrastructure.EventBusLab;

/// <summary>
/// Lab-only append-only journal of EventBus pipeline stages. Observation only — no assertions.
/// </summary>
public sealed class EventBusLabJournal
{
	private readonly ConcurrentQueue<EventBusLabStageRecord> _records = new();

	public IReadOnlyList<EventBusLabStageRecord> Snapshot() => _records.ToArray();

	public void Clear() => _records.Clear();

	public void Record(
		string stage,
		Guid? messageId,
		string? eventType,
		string evidenceKind,
		string? detail = null,
		Guid? orderId = null)
	{
		_records.Enqueue(new EventBusLabStageRecord(
			Utc: DateTimeOffset.UtcNow,
			Stage: stage,
			MessageId: messageId,
			OrderId: orderId,
			EventType: eventType,
			EvidenceKind: evidenceKind,
			Detail: detail));
	}

	public IReadOnlyList<EventBusLabStageRecord> ForMessage(Guid messageId)
		=> _records.Where(r => r.MessageId == messageId).ToList();
}

/// <param name="EvidenceKind"><c>Observed</c> vs <c>Inferred</c>.</param>
public sealed record EventBusLabStageRecord(
	DateTimeOffset Utc,
	string Stage,
	Guid? MessageId,
	Guid? OrderId,
	string? EventType,
	string EvidenceKind,
	string? Detail);

/// <summary>Well-known stage names for Exp 19/20 artifacts.</summary>
public static class EventBusLabStages
{
	public const string AfterPublishBeforeOutboxMark = "AfterPublishBeforeOutboxMark";
	public const string ProcessorEntered = "ProcessorEntered";
	public const string ProcessorCompleted = "ProcessorCompleted";
	public const string SimulatedCrashAfterPublish = "SimulatedCrashAfterPublish";
}
