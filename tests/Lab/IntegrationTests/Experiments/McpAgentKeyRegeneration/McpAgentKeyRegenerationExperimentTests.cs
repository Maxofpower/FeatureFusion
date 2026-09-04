using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Mcp;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.McpAgentKeyRegeneration;

/// <summary>
/// Experiment 13: agent write retry with regenerated MCP idempotency keys.
/// Hypothesis: when an unreliable agent loses a successful <c>orders.create</c> result and
/// retries the same logical intent with new idempotency keys, how much real business-side
/// amplification occurs? Same-key replay should not amplify; regenerated keys are expected
/// to create additional valid orders under the current MCP idempotency contract.
/// Does not prove HTTP Redis idempotency (Exp 3 / BuildingBlocks.Idempotency). Builds on
/// Exp 6 MCP semantics and Exp 10 outbox/handler observation patterns.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpAgentKeyRegenerationExperimentTests
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

	public McpAgentKeyRegenerationExperimentTests(AspireFixture fixture, ITestOutputHelper output)
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
	public async Task Agent_regenerated_idempotency_keys_amplify_mcp_writes_and_downstream_work()
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
		var calls = new List<AgentKeyRegenerationCall>();

		var k1 = System.Ulid.NewUlid().ToString();
		var k2 = System.Ulid.NewUlid().ToString();
		var k3 = System.Ulid.NewUlid().ToString();

		var missK1 = await CallAsync(
			mcp, capture, seenToolTraces, calls,
			behavior: "K1_ConfirmedMiss",
			idempotencyKey: k1);

		var replayK1 = await CallAsync(
			mcp, capture, seenToolTraces, calls,
			behavior: "K1_SameKeyReplay",
			idempotencyKey: k1);

		var agentRetryK2 = await CallAsync(
			mcp, capture, seenToolTraces, calls,
			behavior: "K2_AgentRegeneratedKey",
			idempotencyKey: k2);

		var agentRetryK3 = await CallAsync(
			mcp, capture, seenToolTraces, calls,
			behavior: "K3_AgentRegeneratedKey",
			idempotencyKey: k3);

		var distinctOrderIds = calls
			.Where(c => !c.IsError && c.OrderId != Guid.Empty)
			.Select(c => c.OrderId)
			.Distinct()
			.ToList();

		foreach (var orderId in distinctOrderIds)
		{
			await OrderOutboxObserver.WaitUntilExistsAsync(_services, orderId);
			await Wait.UntilAsync(
				() => _fixture.ProcessedEvents.Any(e => e.OrderId == orderId),
				TimeSpan.FromSeconds(20));
		}

		var replayCompletedUtc = DateTimeOffset.UtcNow;
		await Wait.UntilAsync(
			() => DateTimeOffset.UtcNow - replayCompletedUtc >= ReplayObservationWindow,
			TimeSpan.FromSeconds(10));

		var outboxByOrder = new Dictionary<Guid, int>();
		foreach (var orderId in distinctOrderIds)
			outboxByOrder[orderId] = (await OrderOutboxObserver.FindByOrderIdAsync(_services, orderId)).Count;

		var processedByOrder = distinctOrderIds.ToDictionary(
			id => id,
			id => _fixture.ProcessedEvents.Count(e => e.OrderId == id));

		// AspireFixture.ProcessedEvents is collection-scoped. Clear() drops prior list entries but
		// OutBoxWorker / RabbitMQ may still deliver OrderCreated events from earlier experiments
		// into the same list. Amplification evidence is owned OrderIds only — not global Count.
		var ownedProcessedEvents = _fixture.ProcessedEvents
			.Where(e => distinctOrderIds.Contains(e.OrderId))
			.ToList();
		var foreignProcessedEventCount = _fixture.ProcessedEvents.Count - ownedProcessedEvents.Count;

		var productionCalls = calls.Where(c => c.SawNewToolSpan).ToList();
		var mediatorExecutions = calls.Sum(c => c.MediatorSpanCount);
		var npgsqlExecutions = calls.Sum(c => c.NpgsqlSpanCount);

		var result = new AgentKeyRegenerationExperimentResult(
			Name: "mcp-orders-create-agent-key-regeneration-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new AgentKeyRegenerationConfiguration(
				ToolName,
				McpDefaults.ConfirmedArgument,
				McpDefaults.IdempotencyKeyArgument,
				Store: "MemoryIdempotencyStore (in-process; distinct from HTTP Redis idempotency)",
				AsyncPath: "orders.create → McpInvoker → CreateOrderCommandHandler → outbox_messages → OutBoxWorker → handler",
				ComparedTo: "Experiment 6 (MCP idempotency envelope); Experiment 10 (outbox/handler observation)"),
			Calls: calls,
			Observations: new AgentKeyRegenerationObservations(
				McpCallCount: calls.Count,
				ProductionToolSpanCount: productionCalls.Count,
				DistinctOrderIds: distinctOrderIds,
				MediatorExecutionCount: mediatorExecutions,
				NpgsqlExecutionCount: npgsqlExecutions,
				OutboxRowCountByOrderId: outboxByOrder,
				ProcessedEventCountByOrderId: processedByOrder,
				TotalProcessedEvents: ownedProcessedEvents.Count,
				ForeignProcessedEventCount: foreignProcessedEventCount,
				K1OrderId: missK1.OrderId,
				ReplaySameOrderAsK1: replayK1.OrderId == missK1.OrderId,
				K2DifferentOrderFromK1: agentRetryK2.OrderId != missK1.OrderId,
				K3DifferentOrderFromK1AndK2: agentRetryK3.OrderId != missK1.OrderId
					&& agentRetryK3.OrderId != agentRetryK2.OrderId,
				AmplificationFactor: distinctOrderIds.Count,
				Notes:
				[
					"One logical agent intent ('create this order') simulated with K1, then agent retries with K2/K3 after losing the result.",
					"Regenerated-key retries are client behavior against the idempotency-key contract — not an idempotency bug.",
					$"Transport TraceId={transportTraceId.ToHexString()}; tool traces tracked separately.",
					"ProcessedEvents is test-only handler observation via TestEventHandlerDecorator.",
					"TotalProcessedEvents counts handlers for this experiment's OrderIds only; ForeignProcessedEventCount is late suite contamination on the shared fixture list."
				]));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		missK1.IsError.Should().BeFalse(missK1.Error);
		missK1.OrderId.Should().NotBeEmpty();
		missK1.SawNewToolSpan.Should().BeTrue();
		missK1.MediatorSpanCount.Should().BeGreaterThan(0);
		missK1.NpgsqlSpanCount.Should().BeGreaterThan(0);

		replayK1.IsError.Should().BeFalse(replayK1.Error);
		replayK1.OrderId.Should().Be(missK1.OrderId);
		replayK1.SawNewToolSpan.Should().BeFalse(
			"same-key replay should return cached MCP result without a new mcp.tool span");
		replayK1.MediatorSpanCount.Should().Be(0);
		replayK1.NpgsqlSpanCount.Should().Be(0);

		agentRetryK2.IsError.Should().BeFalse(agentRetryK2.Error);
		agentRetryK2.OrderId.Should().NotBe(missK1.OrderId);
		agentRetryK2.SawNewToolSpan.Should().BeTrue();
		agentRetryK2.MediatorSpanCount.Should().BeGreaterThan(0);
		agentRetryK2.NpgsqlSpanCount.Should().BeGreaterThan(0);

		agentRetryK3.IsError.Should().BeFalse(agentRetryK3.Error);
		agentRetryK3.OrderId.Should().NotBe(missK1.OrderId);
		agentRetryK3.OrderId.Should().NotBe(agentRetryK2.OrderId);
		agentRetryK3.SawNewToolSpan.Should().BeTrue();
		agentRetryK3.MediatorSpanCount.Should().BeGreaterThan(0);
		agentRetryK3.NpgsqlSpanCount.Should().BeGreaterThan(0);

		distinctOrderIds.Should().HaveCount(3,
			"three production writes (K1, K2, K3) should yield three distinct orders. Ids: {0}",
			string.Join(",", distinctOrderIds));

		productionCalls.Should().HaveCount(3,
			"only K1, K2, and K3 should start new mcp.tool spans; K1 replay should not");

		productionCalls.Sum(c => c.MediatorSpanCount).Should().Be(3);
		productionCalls.Should().OnlyContain(c => c.MediatorSpanCount >= 1 && c.NpgsqlSpanCount >= 1,
			"each production tool call should reach Mediator and Npgsql at least once");
		productionCalls.Sum(c => c.NpgsqlSpanCount).Should().BeGreaterThanOrEqualTo(3,
			"K1 may emit more than one Npgsql span per production path (catalog + outbox); total observed was {0}",
			productionCalls.Sum(c => c.NpgsqlSpanCount));

		foreach (var orderId in distinctOrderIds)
		{
			outboxByOrder[orderId].Should().Be(1,
				"each production order should persist exactly one outbox row. OrderId={0}", orderId);
			processedByOrder[orderId].Should().Be(1,
				"each production order should be observed once by ProcessedEvents. OrderId={0}", orderId);
		}

		ownedProcessedEvents.Should().HaveCount(3,
			"exactly three handler observations for this experiment's OrderIds (global ProcessedEvents.Count can include late deliveries from earlier suite tests). Owned={0}; foreign={1}; global={2}",
			ownedProcessedEvents.Count,
			foreignProcessedEventCount,
			_fixture.ProcessedEvents.Count);
		processedByOrder[missK1.OrderId].Should().Be(1,
			"K1 replay and later regenerated-key writes must not duplicate handler work for the first order");
	}

	private async Task<AgentKeyRegenerationCall> CallAsync(
		McpClient mcp,
		InProcessActivityCapture capture,
		HashSet<string> seenToolTraces,
		List<AgentKeyRegenerationCall> calls,
		string behavior,
		string idempotencyKey)
	{
		var args = new Dictionary<string, object?>
		{
			["productId"] = ProductId,
			["quantity"] = Quantity,
			["customerId"] = CustomerId,
			[McpDefaults.IdempotencyKeyArgument] = idempotencyKey,
			[McpDefaults.ConfirmedArgument] = true
		};

		var clock = Stopwatch.StartNew();
		var result = await mcp.CallToolAsync(ToolName, args);
		clock.Stop();

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

		var call = new AgentKeyRegenerationCall(
			RequestNumber: calls.Count + 1,
			Behavior: behavior,
			IdempotencyKey: idempotencyKey,
			IsError: isError,
			Error: errorText,
			OrderId: order?.OrderId ?? Guid.Empty,
			QuantityReturned: order?.Quantity,
			ClientDurationMs: clock.ElapsedMilliseconds,
			ToolTraceId: toolTrace,
			SawNewToolSpan: toolSpan is not null,
			MediatorSpanCount: related.Count(IsMediator),
			NpgsqlSpanCount: related.Count(IsNpgsql));

		calls.Add(call);
		return call;
	}

	private static bool HasToolTag(CapturedActivity span, string toolName) =>
		span.Tags.TryGetValue("mcp.tool.name", out var name) && name == toolName;

	private sealed record AgentKeyRegenerationCall(
		int RequestNumber,
		string Behavior,
		string IdempotencyKey,
		bool IsError,
		string? Error,
		Guid OrderId,
		int? QuantityReturned,
		double ClientDurationMs,
		string? ToolTraceId,
		bool SawNewToolSpan,
		int MediatorSpanCount,
		int NpgsqlSpanCount);

	private sealed record AgentKeyRegenerationConfiguration(
		string Tool,
		string ConfirmedArgument,
		string IdempotencyKeyArgument,
		string Store,
		string AsyncPath,
		string ComparedTo);

	private sealed record AgentKeyRegenerationObservations(
		int McpCallCount,
		int ProductionToolSpanCount,
		IReadOnlyList<Guid> DistinctOrderIds,
		int MediatorExecutionCount,
		int NpgsqlExecutionCount,
		IReadOnlyDictionary<Guid, int> OutboxRowCountByOrderId,
		IReadOnlyDictionary<Guid, int> ProcessedEventCountByOrderId,
		int TotalProcessedEvents,
		int ForeignProcessedEventCount,
		Guid K1OrderId,
		bool ReplaySameOrderAsK1,
		bool K2DifferentOrderFromK1,
		bool K3DifferentOrderFromK1AndK2,
		int AmplificationFactor,
		IReadOnlyList<string> Notes);

	private sealed record AgentKeyRegenerationExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		AgentKeyRegenerationConfiguration Configuration,
		IReadOnlyList<AgentKeyRegenerationCall> Calls,
		AgentKeyRegenerationObservations Observations);
}
