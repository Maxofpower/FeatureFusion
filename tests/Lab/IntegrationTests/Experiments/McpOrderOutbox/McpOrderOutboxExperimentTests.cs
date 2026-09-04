using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Mcp;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.McpOrderOutbox;

/// <summary>
/// Experiment 10: MCP confirmed <c>orders.create</c> → outbox → worker → RabbitMQ → inbox → handler parity.
/// Hypothesis: after a confirmed MCP <c>orders.create</c> cache miss, does the real production workflow
/// persist an <c>outbox_messages</c> row and eventually deliver the same
/// <c>OrderCreatedIntegrationEvent</c> through <c>OutBoxWorker</c>, RabbitMQ, inbox, and handler
/// as the HTTP workflow characterized in Experiment 8?
/// MCP idempotency replay (Exp 6) should not add outbox rows or handler observations.
/// <c>AspireFixture.ProcessedEvents</c> is test observation infrastructure, not production telemetry.
/// Does not claim parity merely because <c>CreateOrderCommandHandler</c> is shared — proves the observed pipeline.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpOrderOutboxExperimentTests
{
	private const string ToolName = "orders.create";
	private const int ProductId = 1;
	private const int CustomerId = 1;
	private const int Quantity = 2;

	private static readonly TimeSpan ReplayObservationWindow = TimeSpan.FromSeconds(5);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public McpOrderOutboxExperimentTests(AspireFixture fixture, ITestOutputHelper output)
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
	public async Task Mcp_confirmed_orders_create_follows_outbox_to_handler_pipeline_and_replay_skips_async_work()
	{
		_fixture.ProcessedEvents.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var (transportTraceId, transportSpanId) = NewTraceParent();
		_http.DefaultRequestHeaders.Remove("traceparent");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"traceparent",
			FormatTraceParent(transportTraceId, transportSpanId));

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var seenToolTraces = new HashSet<string>(StringComparer.Ordinal);
		var baselineKey = System.Ulid.NewUlid().ToString();
		var controlKey = System.Ulid.NewUlid().ToString();
		var calls = new List<McpOrderOutboxCall>();
		var outboxObservations = new List<OutboxSnapshot>();
		var pipelineObservations = new List<PipelineObservation>();

		var miss = await CallMcpAsync(
			mcp,
			capture,
			seenToolTraces,
			calls,
			behavior: "ConfirmedCacheMiss",
			idempotencyKey: baselineKey,
			quantity: Quantity);

		miss.IsError.Should().BeFalse(miss.Error);
		miss.OrderId.Should().NotBeEmpty();
		miss.SawNewToolSpan.Should().BeTrue();
		miss.MediatorSpanCount.Should().BeGreaterThan(0);
		miss.NpgsqlSpanCount.Should().BeGreaterThan(0);

		var outboxAfterCreate = await FindOutboxRowsForOrderIdAsync(miss.OrderId);
		outboxAfterCreate.Should().ContainSingle(
			"confirmed MCP cache miss should persist exactly one outbox row for the order");

		var createdRow = outboxAfterCreate[0];
		outboxObservations.Add(ToSnapshot(createdRow, "AfterOutboxInsert"));

		createdRow.EventType.Should().Be(OrderOutboxObserver.OrderCreatedEventType);
		createdRow.IntegrationEventId.Should().Be(createdRow.OutboxMessageId);
		createdRow.OrderId.Should().Be(miss.OrderId);

		var processedOutbox = await OrderOutboxObserver.WaitUntilProcessedAsync(_services, miss.OrderId);
		outboxObservations.Add(ToSnapshot(processedOutbox, "AfterWorkerProcessed"));

		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == miss.OrderId),
			TimeSpan.FromSeconds(20));

		var handlerEvents = _fixture.ProcessedEvents
			.Where(e => e.OrderId == miss.OrderId)
			.ToList();

		handlerEvents.Should().ContainSingle();
		var delivered = handlerEvents[0];
		delivered.Id.Should().Be(processedOutbox.IntegrationEventId);

		await Wait.UntilAsync(
			() => GetProcessMessageActivities(capture, processedOutbox.IntegrationEventId).Count > 0
				|| CountAllProcessMessageActivities(capture) > 0,
			TimeSpan.FromSeconds(5));

		var inboxAfterDelivery = await WaitForInboxRowAsync(processedOutbox.IntegrationEventId);
		var processMessageActivities = GetProcessMessageActivities(capture, processedOutbox.IntegrationEventId);
		var totalProcessMessageActivities = CountAllProcessMessageActivities(capture);

		pipelineObservations.Add(new PipelineObservation(
			Phase: "AfterAsyncPipeline",
			ObservedUtc: DateTimeOffset.UtcNow,
			OrderId: miss.OrderId,
			IntegrationEventId: processedOutbox.IntegrationEventId,
			OutboxWorkerProcessed: processedOutbox.WorkerProcessed,
			ProcessMessageCount: processMessageActivities.Count,
			ProcessMessageStatuses: processMessageActivities
				.Select(a => GetTag(a, "message.status"))
				.Distinct()
				.ToList(),
			TotalEventBusProcessMessageCount: totalProcessMessageActivities,
			InboxRowCount: inboxAfterDelivery.InboxRowCount,
			InboxProcessed: inboxAfterDelivery.IsProcessed,
			ProcessedEventCountForOrder: handlerEvents.Count,
			Notes:
			[
				$"Outbox Id={processedOutbox.OutboxMessageId} equals IntegrationEvent.Id; OrderId={miss.OrderId}.",
					$"EventBus ProcessMessage spans for message.id={processedOutbox.IntegrationEventId}: {processMessageActivities.Count} (total EventBus ProcessMessage={totalProcessMessageActivities}).",
					processMessageActivities.Count == 0
						? "ProcessMessage Activity was not observed in-process for this outbox-triggered delivery; consumer path inferred from inbox row + ProcessedEvents + outbox processed state."
						: "ProcessMessage Activity observed for the baseline IntegrationEvent.Id.",
				"ProcessedEvents is test-only handler observation via TestEventHandlerDecorator."
			]));

		var outboxCountBeforeReplay = (await FindOutboxRowsForOrderIdAsync(miss.OrderId)).Count;
		var processedCountBeforeReplay = _fixture.ProcessedEvents.Count(e => e.OrderId == miss.OrderId);

		var replay = await CallMcpAsync(
			mcp,
			capture,
			seenToolTraces,
			calls,
			behavior: "McpIdempotencyReplay",
			idempotencyKey: baselineKey,
			quantity: Quantity);

		var replayCompletedUtc = DateTimeOffset.UtcNow;

		await Wait.UntilAsync(
			() =>
			{
				var count = _fixture.ProcessedEvents.Count(e => e.OrderId == miss.OrderId);
				return count == processedCountBeforeReplay
					&& DateTimeOffset.UtcNow - replayCompletedUtc >= ReplayObservationWindow;
			},
			TimeSpan.FromSeconds(20));

		var outboxAfterReplay = await FindOutboxRowsForOrderIdAsync(miss.OrderId);
		outboxObservations.Add(ToSnapshot(outboxAfterReplay[0], "AfterMcpIdempotencyReplay"));

		var control = await CallMcpAsync(
			mcp,
			capture,
			seenToolTraces,
			calls,
			behavior: "ControlNewIdempotencyKey",
			idempotencyKey: controlKey,
			quantity: Quantity);

		control.IsError.Should().BeFalse();
		control.OrderId.Should().NotBe(miss.OrderId);
		control.SawNewToolSpan.Should().BeTrue();
		control.MediatorSpanCount.Should().BeGreaterThan(0);
		control.NpgsqlSpanCount.Should().BeGreaterThan(0);

		var controlOutbox = await FindOutboxRowsForOrderIdAsync(control.OrderId);
		controlOutbox.Should().ContainSingle();

		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == control.OrderId),
			TimeSpan.FromSeconds(20));

		var result = new McpOrderOutboxExperimentResult(
			Name: "mcp-order-outbox-parity-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new McpOrderOutboxConfiguration(
				ToolName,
				McpDefaults.ConfirmedArgument,
				McpDefaults.IdempotencyKeyArgument,
				McpIdempotencyStore: "MemoryIdempotencyStore (in-process; distinct from HTTP Redis idempotency)",
				AsyncPath: "orders.create → McpInvoker → CreateOrderCommandHandler → IntegrationEventService → outbox_messages → OutBoxWorker → PublishDirect → RabbitMQ → MessageProcessor → inbox_messages → OrderCreatedIntegrationEventHandler",
				ComparedTo: "Experiment 8 HTTP outbox lifecycle fingerprint",
				ProcessedEventsNote: "AspireFixture.ProcessedEvents is test-only handler observation"),
			Calls: calls,
			OutboxObservations: outboxObservations,
			PipelineObservations: pipelineObservations,
			Observations: new McpOrderOutboxObservations(
				BaselineOrderId: miss.OrderId,
				BaselineIntegrationEventId: processedOutbox.IntegrationEventId,
				OutboxCountAfterMcpMiss: outboxAfterCreate.Count,
				OutboxInitiallyPending: createdRow.WorkerPending,
				OutboxAlreadyProcessedAtCreateObservation: createdRow.WorkerProcessed,
				OutboxWorkerProcessed: processedOutbox.WorkerProcessed,
				ProcessMessageCountForBaseline: processMessageActivities.Count,
				TotalEventBusProcessMessageCount: totalProcessMessageActivities,
				ConsumerEvidenceFromProcessMessageSpan: processMessageActivities.Count > 0,
				ConsumerEvidenceFromInboxAndHandler: inboxAfterDelivery.InboxRowCount == 1 && handlerEvents.Count == 1,
				InboxRowCountForBaseline: inboxAfterDelivery.InboxRowCount,
				InboxProcessedForBaseline: inboxAfterDelivery.IsProcessed,
				ProcessedEventCountAfterBaseline: handlerEvents.Count,
				ReplaySameOrderId: replay.OrderId == miss.OrderId,
				ReplaySawNewToolSpan: replay.SawNewToolSpan,
				ReplayMediatorSpans: replay.MediatorSpanCount,
				ReplayNpgsqlSpans: replay.NpgsqlSpanCount,
				OutboxCountBeforeReplay: outboxCountBeforeReplay,
				OutboxCountAfterReplay: outboxAfterReplay.Count,
				ProcessedEventCountAfterReplay: _fixture.ProcessedEvents.Count(e => e.OrderId == miss.OrderId),
				ControlOrderId: control.OrderId,
				ControlIntegrationEventId: controlOutbox[0].IntegrationEventId,
				ControlOutboxCount: controlOutbox.Count,
				ControlProcessedEventCount: _fixture.ProcessedEvents.Count(e => e.OrderId == control.OrderId),
				Notes:
				[
					$"Transport TraceId={transportTraceId.ToHexString()}; tool traces tracked separately from transport.",
					$"MCP miss toolTrace={miss.ToolTraceId}; replay newToolSpan={replay.SawNewToolSpan}.",
					"Does not claim exactly-once RabbitMQ delivery or worker crash consistency.",
					"Intermediate Pending outbox state may be skipped if OutBoxWorker poll wins immediately after HTTP-equivalent timing."
				]));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		replay.IsError.Should().BeFalse();
		replay.OrderId.Should().Be(miss.OrderId);
		replay.SawNewToolSpan.Should().BeFalse(
			"MCP memory idempotency replay should not start a new mcp.tool span");
		replay.MediatorSpanCount.Should().Be(0);
		replay.NpgsqlSpanCount.Should().Be(0);

		outboxAfterReplay.Should().ContainSingle();
		outboxAfterReplay[0].OutboxMessageId.Should().Be(processedOutbox.OutboxMessageId);
		_fixture.ProcessedEvents.Count(e => e.OrderId == miss.OrderId).Should().Be(1);

		controlOutbox[0].IntegrationEventId.Should().NotBe(processedOutbox.IntegrationEventId);
		_fixture.ProcessedEvents.Count(e => e.OrderId == control.OrderId).Should().Be(1);

		var consumerEvidenceObserved = processMessageActivities.Count > 0
			|| (inboxAfterDelivery.InboxRowCount == 1
				&& handlerEvents.Count == 1
				&& processedOutbox.WorkerProcessed);
		consumerEvidenceObserved.Should().BeTrue(
			"OutBoxWorker → RabbitMQ → consumer should leave inbox/handler evidence; ProcessMessage span is optional in-process telemetry");
		inboxAfterDelivery.InboxRowCount.Should().Be(1);
		delivered.Id.Should().Be(processedOutbox.IntegrationEventId);
	}

	private async Task<McpOrderOutboxCall> CallMcpAsync(
		McpClient mcp,
		InProcessActivityCapture capture,
		HashSet<string> seenToolTraces,
		List<McpOrderOutboxCall> calls,
		string behavior,
		string idempotencyKey,
		int quantity)
	{
		var args = new Dictionary<string, object?>
		{
			["productId"] = ProductId,
			["quantity"] = quantity,
			["customerId"] = CustomerId,
			[McpDefaults.IdempotencyKeyArgument] = idempotencyKey,
			[McpDefaults.ConfirmedArgument] = true
		};

		var clock = Stopwatch.StartNew();
		var result = await mcp.CallToolAsync(ToolName, args);
		clock.Stop();
		var completedUtc = DateTimeOffset.UtcNow;

		var isError = result.IsError ?? false;
		var errorText = isError ? McpToolResults.Truncate(McpToolResults.GetText(result)) : null;
		var order = !isError ? McpToolResults.TryParseOrder(result, JsonOptions) : null;

		var toolSpan = capture.All.FirstOrDefault(s =>
			s.DisplayName == "mcp.tool"
			&& HasToolTag(s, ToolName)
			&& seenToolTraces.Add(s.TraceId));

		var toolTrace = toolSpan?.TraceId;
		var related = toolTrace is null
			? []
			: capture.All.Where(s => s.TraceId == toolTrace).ToList();

		var call = new McpOrderOutboxCall(
			RequestNumber: calls.Count + 1,
			Behavior: behavior,
			IdempotencyKey: idempotencyKey,
			Quantity: quantity,
			IsError: isError,
			Error: errorText,
			OrderId: order?.OrderId ?? Guid.Empty,
			QuantityReturned: order?.Quantity,
			CompletedUtc: completedUtc,
			ClientDurationMs: clock.ElapsedMilliseconds,
			ToolTraceId: toolTrace,
			SawNewToolSpan: toolSpan is not null,
			MediatorSpanCount: related.Count(IsMediator),
			NpgsqlSpanCount: related.Count(IsNpgsql));

		calls.Add(call);
		return call;
	}

	private async Task<IReadOnlyList<OutboxRowObservation>> FindOutboxRowsForOrderIdAsync(Guid orderId)
	{
		var rows = await OrderOutboxObserver.FindByOrderIdAsync(_services, orderId);
		return rows.Select(MapObservation).ToList();
	}

	private async Task<InboxObservation> QueryInboxAsync(Guid integrationEventId)
	{
		await using var scope = _services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

		var inboxRows = await db.InboxMessages
			.AsNoTracking()
			.Where(m => m.Id == integrationEventId)
			.ToListAsync();

		var row = inboxRows.SingleOrDefault();

		return new InboxObservation(
			InboxRowCount: inboxRows.Count,
			InboxStatus: row?.Status.ToString(),
			IsProcessed: row?.IsProcessed ?? false,
			ProcessedAtUtc: row?.ProcessedAt);
	}

	private async Task<InboxObservation> WaitForInboxRowAsync(Guid integrationEventId)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(20))
		{
			var observation = await QueryInboxAsync(integrationEventId);
			if (observation.InboxRowCount == 1)
				return observation;

			await Task.Delay(100);
		}

		throw new TimeoutException(
			$"Inbox message {integrationEventId} was not stored within timeout");
	}

	private static int CountAllProcessMessageActivities(InProcessActivityCapture capture) =>
		capture.All.Count(span =>
			span.Source == "EventBus"
			&& span.DisplayName == "ProcessMessage");

	private static IReadOnlyList<CapturedActivity> GetProcessMessageActivities(
		InProcessActivityCapture capture,
		Guid integrationEventId)
	{
		var id = integrationEventId.ToString();
		return capture.All
			.Where(span =>
				span.Source == "EventBus"
				&& span.DisplayName == "ProcessMessage"
				&& span.Tags.TryGetValue("message.id", out var mid)
				&& mid == id)
			.ToList();
	}

	private static OutboxSnapshot ToSnapshot(OrderOutboxRow row, string phase) =>
		ToSnapshot(MapObservation(row), phase);

	private static OutboxSnapshot ToSnapshot(OutboxRowObservation row, string phase) =>
		new(
			Phase: phase,
			ObservedUtc: DateTimeOffset.UtcNow,
			OutboxMessageId: row.OutboxMessageId,
			IntegrationEventId: row.IntegrationEventId,
			OrderId: row.OrderId,
			Total: row.Total,
			EventType: row.EventType,
			Status: row.Status,
			ProcessedAtUtc: row.ProcessedAtUtc,
			CompletedAtUtc: row.CompletedAtUtc,
			RetryCount: row.RetryCount,
			WorkerPending: row.WorkerPending,
			WorkerProcessed: row.WorkerProcessed,
			CreatedAtUtc: row.CreatedAtUtc);

	private static OutboxRowObservation MapObservation(OrderOutboxRow row) =>
		new(
			OutboxMessageId: row.OutboxMessageId,
			IntegrationEventId: row.IntegrationEventId,
			OrderId: row.OrderId,
			Total: row.Total,
			EventType: row.EventType,
			Status: row.Status,
			ProcessedAtUtc: row.ProcessedAtUtc,
			CompletedAtUtc: row.CompletedAtUtc,
			RetryCount: row.RetryCount,
			WorkerPending: row.WorkerPending,
			WorkerProcessed: row.WorkerProcessed,
			CreatedAtUtc: row.CreatedAtUtc);

	private static bool HasToolTag(CapturedActivity span, string toolName) =>
		span.Tags.TryGetValue("mcp.tool.name", out var name) && name == toolName;

	private static string? GetTag(CapturedActivity activity, string key) =>
		activity.Tags.TryGetValue(key, out var value) ? value : null;

	private sealed record McpOrderOutboxCall(
		int RequestNumber,
		string Behavior,
		string IdempotencyKey,
		int Quantity,
		bool IsError,
		string? Error,
		Guid OrderId,
		int? QuantityReturned,
		DateTimeOffset CompletedUtc,
		double ClientDurationMs,
		string? ToolTraceId,
		bool SawNewToolSpan,
		int MediatorSpanCount,
		int NpgsqlSpanCount);

	private sealed record OutboxRowObservation(
		Guid OutboxMessageId,
		Guid IntegrationEventId,
		Guid OrderId,
		decimal Total,
		string EventType,
		string Status,
		DateTime? ProcessedAtUtc,
		DateTime? CompletedAtUtc,
		int RetryCount,
		bool WorkerPending,
		bool WorkerProcessed,
		DateTime CreatedAtUtc);

	private sealed record OutboxSnapshot(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid OutboxMessageId,
		Guid IntegrationEventId,
		Guid OrderId,
		decimal Total,
		string EventType,
		string Status,
		DateTime? ProcessedAtUtc,
		DateTime? CompletedAtUtc,
		int RetryCount,
		bool WorkerPending,
		bool WorkerProcessed,
		DateTime CreatedAtUtc);

	private sealed record InboxObservation(
		int InboxRowCount,
		string? InboxStatus,
		bool IsProcessed,
		DateTime? ProcessedAtUtc);

	private sealed record PipelineObservation(
		string Phase,
		DateTimeOffset ObservedUtc,
		Guid OrderId,
		Guid IntegrationEventId,
		bool OutboxWorkerProcessed,
		int ProcessMessageCount,
		IReadOnlyList<string?> ProcessMessageStatuses,
		int TotalEventBusProcessMessageCount,
		int InboxRowCount,
		bool InboxProcessed,
		int ProcessedEventCountForOrder,
		IReadOnlyList<string> Notes);

	private sealed record McpOrderOutboxConfiguration(
		string Tool,
		string ConfirmedArgument,
		string IdempotencyKeyArgument,
		string McpIdempotencyStore,
		string AsyncPath,
		string ComparedTo,
		string ProcessedEventsNote);

	private sealed record McpOrderOutboxObservations(
		Guid BaselineOrderId,
		Guid BaselineIntegrationEventId,
		int OutboxCountAfterMcpMiss,
		bool OutboxInitiallyPending,
		bool OutboxAlreadyProcessedAtCreateObservation,
		bool OutboxWorkerProcessed,
		int ProcessMessageCountForBaseline,
		int TotalEventBusProcessMessageCount,
		bool ConsumerEvidenceFromProcessMessageSpan,
		bool ConsumerEvidenceFromInboxAndHandler,
		int InboxRowCountForBaseline,
		bool InboxProcessedForBaseline,
		int ProcessedEventCountAfterBaseline,
		bool ReplaySameOrderId,
		bool ReplaySawNewToolSpan,
		int ReplayMediatorSpans,
		int ReplayNpgsqlSpans,
		int OutboxCountBeforeReplay,
		int OutboxCountAfterReplay,
		int ProcessedEventCountAfterReplay,
		Guid ControlOrderId,
		Guid ControlIntegrationEventId,
		int ControlOutboxCount,
		int ControlProcessedEventCount,
		IReadOnlyList<string> Notes);

	private sealed record McpOrderOutboxExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		McpOrderOutboxConfiguration Configuration,
		IReadOnlyList<McpOrderOutboxCall> Calls,
		IReadOnlyList<OutboxSnapshot> OutboxObservations,
		IReadOnlyList<PipelineObservation> PipelineObservations,
		McpOrderOutboxObservations Observations);
}
