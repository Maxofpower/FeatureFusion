using EventBusRabbitMQ.Domain;
using EventBusRabbitMQ.Infrastructure.Messaging;
using RabbitMQ.Client.Events;

namespace IntegrationTests.Infrastructure.EventBusLab;

/// <summary>
/// Lab-only decorator around <see cref="IMessageProcessor"/>. Journals enter/exit only —
/// does not change processing results.
/// </summary>
public sealed class LabMessageProcessorDecorator : IMessageProcessor
{
	private readonly IMessageProcessor _inner;
	private readonly EventBusLabJournal _journal;

	public LabMessageProcessorDecorator(IMessageProcessor inner, EventBusLabJournal journal)
	{
		_inner = inner;
		_journal = journal;
	}

	public async Task<ProcessingResult> ProcessMessageAsync(
		BasicDeliverEventArgs args,
		bool deduplication)
	{
		var messageId = TryParseMessageId(args);
		var eventType = args.RoutingKey;

		_journal.Record(
			EventBusLabStages.ProcessorEntered,
			messageId,
			eventType,
			evidenceKind: "Observed",
			detail: $"deduplicationFlag={deduplication}; redelivered={args.Redelivered}");

		var result = await _inner.ProcessMessageAsync(args, deduplication).ConfigureAwait(false);

		_journal.Record(
			EventBusLabStages.ProcessorCompleted,
			messageId,
			eventType,
			evidenceKind: "Observed",
			detail: $"result={result}; deduplicationFlag={deduplication}");

		return result;
	}

	private static Guid? TryParseMessageId(BasicDeliverEventArgs args)
	{
		var raw = args.BasicProperties?.MessageId;
		return Guid.TryParse(raw, out var id) ? id : null;
	}
}
