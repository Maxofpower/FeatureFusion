using System.Diagnostics;
using System.Collections.Concurrent;
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
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;

using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.McpConcurrentSameKey;

/// <summary>
/// Experiment 14: concurrent agent behavior — the same MCP <c>orders.create</c> write sent concurrently
/// with identical arguments and a shared idempotency key (<c>K1</c>).
/// Hypothesis: MCP idempotency + in-flight gating results in exactly one production business operation
/// (one order, one outbox row, one downstream handler observation), with other callers replaying.
/// This experiment characterizes behavior; it does not change MCP or any BuildingBlock.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpConcurrentSameKeyExperimentTests
{
	private const string ToolName = "orders.create";
	private const int ProductId = 1;
	private const int CustomerId = 1;
	private const int Quantity = 2;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public McpConcurrentSameKeyExperimentTests(AspireFixture fixture, ITestOutputHelper output)
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
	public async Task Concurrent_same_key_mcp_write_produces_exactly_one_business_operation()
	{
		_fixture.ProcessedEvents.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var (transportTraceId, transportSpanId) = NewTraceParent();
		_http.DefaultRequestHeaders.Remove("traceparent");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"traceparent",
			FormatTraceParent(transportTraceId, transportSpanId));

		var k1 = System.Ulid.NewUlid().ToString();
		var seenToolTraces = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
		var transportTraceIdHex = transportTraceId.ToHexString();
		var calls = await StartTogether(
			count: 3,
			send: i => CallOnceAsync(
				requestNumber: i + 1,
				behavior: $"ConcurrentCall_{i + 1}",
				idempotencyKey: k1,
				capture,
				transportTraceIdHex,
				seenToolTraces));

		var distinctOrderIds = calls
			.Where(c => !c.IsError && c.OrderId != Guid.Empty)
			.Select(c => c.OrderId)
			.Distinct()
			.ToList();

		// Tool-level production execution: only the first concurrent in-flight winner should execute mcp.tool.
		var toolTraces = capture.All
			.Where(s => s.DisplayName == "mcp.tool" && HasToolTag(s, ToolName))
			.Select(s => s.TraceId)
			.Distinct()
			.ToList();

		var productionToolExecutionCount = toolTraces.Count;

		// If production is exactly-once for this write key, all successful callers should get the same orderId.
		var expectedSingleOrder = distinctOrderIds.SingleOrDefault();
		calls.Where(c => !c.IsError).Select(c => c.OrderId).Distinct().Count().Should().Be(distinctOrderIds.Count,
			"orderId uniqueness should match distinctOrderIds derived from successful callers");

		// Outbox + downstream handler evidence.
		var outboxByOrderId = new Dictionary<Guid, IReadOnlyList<OutboxRowObservation>>();
		var processedCountByOrderId = new Dictionary<Guid, int>();

		foreach (var orderId in distinctOrderIds)
		{
			var outboxRows = MapObservations(await OrderOutboxObserver.WaitUntilAnyAsync(_services, orderId));
			outboxByOrderId[orderId] = outboxRows;

			await Wait.UntilAsync(
				() => _fixture.ProcessedEvents.Any(e => e.OrderId == orderId),
				TimeSpan.FromSeconds(20));

			processedCountByOrderId[orderId] = _fixture.ProcessedEvents.Count(e => e.OrderId == orderId);
		}

		var totalProcessedEvents = _fixture.ProcessedEvents.Count;
		var totalOutboxRows = outboxByOrderId.Values.Sum(list => list.Count);

		var result = new
		{
			name = "mcp-orders-create-concurrent-same-key-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			environment = "Development",
			configuration = new
			{
				Tool = ToolName,
				ConfirmedArgument = McpDefaults.ConfirmedArgument,
				IdempotencyKeyArgument = McpDefaults.IdempotencyKeyArgument,
				Concurrency = "3 parallel calls",
				SharedIdempotencyKey = k1,
				Store = "MemoryIdempotencyStore (in-process)",
				AsyncPath = "orders.create → McpInvoker → CreateOrderCommandHandler → outbox → OutBoxWorker → handler",
			},
			transport = new
			{
				traceparent = transportTraceId.ToHexString(),
				toolExecutionCount = productionToolExecutionCount
			},
			calls,
			observations = new
			{
				DistinctOrderIds = distinctOrderIds,
				OutboxRowCountByOrderId = outboxByOrderId.ToDictionary(
					kvp => kvp.Key.ToString(),
					kvp => kvp.Value.Count),
				ProcessedEventCountByOrderId = processedCountByOrderId.ToDictionary(
					kvp => kvp.Key.ToString(),
					kvp => kvp.Value),
				TotalOutboxRows = totalOutboxRows,
				TotalProcessedEvents = totalProcessedEvents
			}
		};

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		// Critical assertions.
		calls.Should().OnlyContain(c => !c.IsError, "concurrent idempotency waiters should replay, not fail");
		distinctOrderIds.Should().HaveCount(1, "concurrent same-key MCP calls should converge to a single business order. Orders: {0}",
			string.Join(",", distinctOrderIds));

		productionToolExecutionCount.Should().Be(1,
			"only one concurrent invoker should execute mcp.tool for a shared idempotency key");

		var winnerOrderId = expectedSingleOrder;
		outboxByOrderId.Should().ContainKey(winnerOrderId);
		outboxByOrderId[winnerOrderId].Should().HaveCount(1, "one production order should persist exactly one outbox row. OrderId={0}", winnerOrderId);

		processedCountByOrderId.Should().ContainKey(winnerOrderId);
		processedCountByOrderId[winnerOrderId].Should().Be(1,
			"downstream handler observation should happen exactly once for the winner order. OrderId={0}", winnerOrderId);

		// Compare guidance (no code change): Exp 4 is HTTP/Redis, Exp 6 is same-key replay, Exp 13 is regenerated-key amplification.
		// If this fails, MCP concurrency behavior is inconsistent with already-proven expectations.
	}

	private async Task<McpConcurrentSameKeyCall> CallOnceAsync(
		int requestNumber,
		string behavior,
		string idempotencyKey,
		InProcessActivityCapture capture,
		string transportTraceId,
		ConcurrentDictionary<string, byte> seenToolTraces)
	{
		// Create a new MCP client per task to avoid client concurrency assumptions.
		await using var mcp = await LabMcpClient.CreateAsync(_http);

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
		var errorCode = isError ? TryReadErrorCode(result) : null;
		var errorText = isError ? McpToolResults.Truncate(McpToolResults.GetText(result)) : null;
		var order = !isError ? McpToolResults.TryParseOrder(result, JsonOptions) : null;

		// Claim the (at most) one production mcp.tool trace in a thread-safe way.
		// In a successful same-key concurrent run, cached waiters should not execute mcp.tool,
		// so they should remain SawNewToolSpan=false and have mediator/npdgsql counts of 0.
		var toolSpan = capture.All.FirstOrDefault(s =>
			s.DisplayName == "mcp.tool"
			&& HasToolTag(s, ToolName)
			&& seenToolTraces.TryAdd(s.TraceId, 0));

		var toolTraceId = toolSpan?.TraceId;
		var related = toolTraceId is null
			? []
			: capture.All.Where(s => s.TraceId == toolTraceId).ToList();

		return new McpConcurrentSameKeyCall(
			RequestNumber: requestNumber,
			Behavior: behavior,
			IdempotencyKey: idempotencyKey,
			IsError: isError,
			ErrorCode: errorCode,
			Error: errorText,
			OrderId: order?.OrderId ?? Guid.Empty,
			QuantityReturned: order?.Quantity,
			ClientDurationMs: clock.ElapsedMilliseconds,
			TransportTraceId: transportTraceId,
			SawNewToolSpan: toolSpan is not null,
			MediatorSpanCount: related.Count(IsMediator),
			NpgsqlSpanCount: related.Count(IsNpgsql));
	}

	private static IReadOnlyList<OutboxRowObservation> MapObservations(IReadOnlyList<OrderOutboxRow> rows) =>
		rows.Select(row => new OutboxRowObservation(
			OutboxMessageId: row.OutboxMessageId,
			IntegrationEventId: row.IntegrationEventId,
			OrderId: row.OrderId,
			Status: row.Status,
			ProcessedAtUtc: row.ProcessedAtUtc,
			CompletedAtUtc: row.CompletedAtUtc)).ToList();

	private static string? TryReadErrorCode(CallToolResult result)
	{
		var code = McpToolResults.TryReadJsonErrorCode(result);
		if (code is not null)
			return code;

		var text = McpToolResults.GetText(result);
		return text.Contains("ConfirmationRequired", StringComparison.OrdinalIgnoreCase)
			? nameof(McpErrorCode.ConfirmationRequired)
			: null;
	}

	private static bool HasToolTag(CapturedActivity span, string toolName) =>
		span.Tags.TryGetValue("mcp.tool.name", out var name) && name == toolName;

	private static async Task<McpConcurrentSameKeyCall[]> StartTogether(
		int count,
		Func<int, Task<McpConcurrentSameKeyCall>> send)
	{
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var tasks = Enumerable.Range(0, count).Select(async i =>
		{
			await gate.Task;
			return await send(i);
		}).ToArray();
		gate.SetResult();
		return await Task.WhenAll(tasks);
	}

	private sealed record OutboxRowObservation(
		Guid OutboxMessageId,
		Guid IntegrationEventId,
		Guid OrderId,
		string Status,
		DateTime? ProcessedAtUtc,
		DateTime? CompletedAtUtc);

	private sealed record McpConcurrentSameKeyCall(
		int RequestNumber,
		string Behavior,
		string IdempotencyKey,
		bool IsError,
		string? ErrorCode,
		string? Error,
		Guid OrderId,
		int? QuantityReturned,
		long ClientDurationMs,
		string TransportTraceId,
		bool SawNewToolSpan,
		int MediatorSpanCount,
		int NpgsqlSpanCount);
}

