using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Mcp;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.McpOrderIdempotency;

/// <summary>
/// Experiment 6: MCP <c>orders.create</c> confirmation gate and in-memory idempotency envelope.
/// Hypothesis: the MCP surface uses <see cref="MemoryIdempotencyStore"/> and
/// <c>RequireConfirmation</c> semantics that differ from the HTTP Redis
/// <c>IdempotentAttributeFilter</c> path in Experiment 3.
/// Investigates confirmation gating, idempotency-key replay, replay bypass of
/// <c>CreateOrderCommand</c>, same-key/different-args behavior, and fresh execution on a new key.
/// Does not re-prove HTTP Redis idempotency (Exp 3), outbox/async handler delivery (Exp 5),
/// or gateway behavior.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpOrderIdempotencyExperimentTests
{
	private const string ToolName = "orders.create";
	private const int ProductId = 1;
	private const int CustomerId = 1;
	private const int BaselineQuantity = 2;
	private const int MutatedQuantity = 5;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public McpOrderIdempotencyExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_output = output;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Mcp_orders_create_confirmation_and_memory_idempotency_are_observed()
	{
		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var (transportTraceId, transportSpanId) = NewTraceParent();
		_http.DefaultRequestHeaders.Remove("traceparent");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"traceparent",
			FormatTraceParent(transportTraceId, transportSpanId));

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var seenToolTraces = new HashSet<string>(StringComparer.Ordinal);
		var calls = new List<McpOrderIdempotencyCall>();
		var cachedKey = System.Ulid.NewUlid().ToString();
		var freshKey = System.Ulid.NewUlid().ToString();

		var requestNumber = 0;

		calls.Add(await CallAsync(
			mcp,
			capture,
			seenToolTraces,
			++requestNumber,
			behavior: "Unconfirmed",
			idempotencyKey: System.Ulid.NewUlid().ToString(),
			confirmed: false,
			quantity: BaselineQuantity));

		calls.Add(await CallAsync(
			mcp,
			capture,
			seenToolTraces,
			++requestNumber,
			behavior: "ConfirmedMiss",
			idempotencyKey: cachedKey,
			confirmed: true,
			quantity: BaselineQuantity));

		calls.Add(await CallAsync(
			mcp,
			capture,
			seenToolTraces,
			++requestNumber,
			behavior: "SameKeyReplay",
			idempotencyKey: cachedKey,
			confirmed: true,
			quantity: BaselineQuantity));

		calls.Add(await CallAsync(
			mcp,
			capture,
			seenToolTraces,
			++requestNumber,
			behavior: "SameKeyDifferentQuantity",
			idempotencyKey: cachedKey,
			confirmed: true,
			quantity: MutatedQuantity));

		calls.Add(await CallAsync(
			mcp,
			capture,
			seenToolTraces,
			++requestNumber,
			behavior: "NewKeyFresh",
			idempotencyKey: freshKey,
			confirmed: true,
			quantity: MutatedQuantity));

		var unconfirmed = calls[0];
		var miss = calls[1];
		var replay = calls[2];
		var mutated = calls[3];
		var fresh = calls[4];

		var result = new McpOrderIdempotencyExperimentResult(
			Name: "mcp-orders-create-idempotency-v1",
			StartedUtc: startedUtc,
			GitSha: LabRunInfo.ReadGitSha(),
			Environment: "Development",
			Configuration: new McpOrderIdempotencyConfiguration(
				ToolName,
				McpDefaults.ConfirmedArgument,
				McpDefaults.IdempotencyKeyArgument,
				Store: "MemoryIdempotencyStore (UseMemoryIdempotency, TTL 1h)",
				CacheKey: "toolName + U+001F + idempotencyKey (body not included)",
				Command: "CreateOrderCommand → CreateOrderCommandHandler"),
			Calls: calls,
			Observations: new McpOrderIdempotencyObservations(
				UnconfirmedIsError: unconfirmed.IsError,
				UnconfirmedErrorCode: unconfirmed.ErrorCode,
				UnconfirmedSawNewToolSpan: unconfirmed.SawNewToolSpan,
				UnconfirmedMediatorSpans: unconfirmed.MediatorSpanCount,
				UnconfirmedNpgsqlSpans: unconfirmed.NpgsqlSpanCount,
				MissIsError: miss.IsError,
				MissOrderId: miss.OrderId,
				MissQuantity: miss.Quantity,
				MissSawNewToolSpan: miss.SawNewToolSpan,
				MissMediatorSpans: miss.MediatorSpanCount,
				MissNpgsqlSpans: miss.NpgsqlSpanCount,
				ReplayIsError: replay.IsError,
				ReplayOrderId: replay.OrderId,
				ReplayQuantity: replay.Quantity,
				ReplaySawNewToolSpan: replay.SawNewToolSpan,
				ReplayMediatorSpans: replay.MediatorSpanCount,
				ReplayNpgsqlSpans: replay.NpgsqlSpanCount,
				ReplaySameOrderIdAsMiss: replay.OrderId == miss.OrderId && miss.OrderId != Guid.Empty,
				MutatedIsError: mutated.IsError,
				MutatedOrderId: mutated.OrderId,
				MutatedQuantity: mutated.Quantity,
				MutatedSawNewToolSpan: mutated.SawNewToolSpan,
				MutatedMediatorSpans: mutated.MediatorSpanCount,
				MutatedNpgsqlSpans: mutated.NpgsqlSpanCount,
				MutatedKeptOriginalQuantity: mutated.Quantity == miss.Quantity,
				FreshIsError: fresh.IsError,
				FreshOrderId: fresh.OrderId,
				FreshQuantity: fresh.Quantity,
				FreshSawNewToolSpan: fresh.SawNewToolSpan,
				FreshMediatorSpans: fresh.MediatorSpanCount,
				FreshNpgsqlSpans: fresh.NpgsqlSpanCount,
				FreshDifferentOrderId: fresh.OrderId != miss.OrderId && fresh.OrderId != Guid.Empty,
				Notes:
				[
					$"Transport TraceId={transportTraceId.ToHexString()}.",
					$"Unconfirmed: isError={unconfirmed.IsError}, errorCode={unconfirmed.ErrorCode ?? "(none)"}, newToolSpan={unconfirmed.SawNewToolSpan}.",
					$"ConfirmedMiss toolTrace={miss.ToolTraceId}; mediator={miss.MediatorSpanCount}; npgsql={miss.NpgsqlSpanCount}.",
					$"SameKeyReplay toolTrace={replay.ToolTraceId ?? "(none)"}; newToolSpan={replay.SawNewToolSpan}; cached quantity={replay.Quantity}.",
					$"SameKeyDifferentQuantity requested={MutatedQuantity}, returned quantity={mutated.Quantity}, orderId={mutated.OrderId}.",
					$"NewKeyFresh orderId={fresh.OrderId}, quantity={fresh.Quantity}.",
					"MCP idempotency cache key is namespaced by tool + idempotencyKey only; request body changes do not bust the cache."
				]));

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		unconfirmed.IsError.Should().BeTrue(
			"orders.create requires confirmed=true before execution");
		unconfirmed.ErrorCode.Should().Be("ConfirmationRequired",
			"McpInvoker rejects unconfirmed writes with {0}", unconfirmed.Error);
		unconfirmed.SawNewToolSpan.Should().BeFalse(
			"confirmation failure returns before InvokeCoreAsync / mcp.tool");
		unconfirmed.MediatorSpanCount.Should().Be(0);
		unconfirmed.NpgsqlSpanCount.Should().Be(0);

		miss.IsError.Should().BeFalse(miss.Error);
		miss.OrderId.Should().NotBeEmpty();
		miss.Quantity.Should().Be(BaselineQuantity);
		miss.SawNewToolSpan.Should().BeTrue();
		miss.MediatorSpanCount.Should().BeGreaterThan(0,
			"confirmed miss should dispatch CreateOrderCommand. Tool trace: {0}",
			miss.ToolTraceId);
		miss.NpgsqlSpanCount.Should().BeGreaterThan(0,
			"confirmed miss should persist catalog/outbox. Tool trace: {0}",
			miss.ToolTraceId);

		replay.IsError.Should().BeFalse(replay.Error);
		replay.OrderId.Should().Be(miss.OrderId);
		replay.Quantity.Should().Be(BaselineQuantity);
		replay.SawNewToolSpan.Should().BeFalse(
			"memory idempotency hit returns cached payload without starting mcp.tool");
		replay.MediatorSpanCount.Should().Be(0);
		replay.NpgsqlSpanCount.Should().Be(0);

		mutated.IsError.Should().BeFalse(mutated.Error);
		mutated.OrderId.Should().Be(miss.OrderId);
		mutated.Quantity.Should().Be(BaselineQuantity,
			"MemoryIdempotencyStore replays by tool+key, not by serialized command body");
		mutated.SawNewToolSpan.Should().BeFalse();
		mutated.MediatorSpanCount.Should().Be(0);
		mutated.NpgsqlSpanCount.Should().Be(0);

		fresh.IsError.Should().BeFalse(fresh.Error);
		fresh.OrderId.Should().NotBe(miss.OrderId);
		fresh.Quantity.Should().Be(MutatedQuantity);
		fresh.SawNewToolSpan.Should().BeTrue();
		fresh.MediatorSpanCount.Should().BeGreaterThan(0);
		fresh.NpgsqlSpanCount.Should().BeGreaterThan(0);
	}

	private async Task<McpOrderIdempotencyCall> CallAsync(
		McpClient mcp,
		InProcessActivityCapture capture,
		HashSet<string> seenToolTraces,
		int requestNumber,
		string behavior,
		string idempotencyKey,
		bool confirmed,
		int quantity)
	{
		var args = new Dictionary<string, object?>
		{
			["productId"] = ProductId,
			["quantity"] = quantity,
			["customerId"] = CustomerId,
			[McpDefaults.IdempotencyKeyArgument] = idempotencyKey
		};
		if (confirmed)
			args[McpDefaults.ConfirmedArgument] = true;

		var clock = Stopwatch.StartNew();
		var result = await mcp.CallToolAsync(ToolName, args);
		clock.Stop();

		var isError = result.IsError ?? false;
		var errorText = isError ? McpToolResults.Truncate(McpToolResults.GetText(result)) : null;
		var errorCode = isError ? TryReadErrorCode(result) : null;
		var order = !isError ? McpToolResults.TryParseOrder(result, JsonOptions) : null;

		var toolSpan = capture.All.FirstOrDefault(s =>
			s.DisplayName == "mcp.tool"
			&& HasTag(s, "mcp.tool.name", ToolName)
			&& seenToolTraces.Add(s.TraceId));

		var toolTrace = toolSpan?.TraceId;
		var related = toolTrace is null
			? []
			: capture.All.Where(s => s.TraceId == toolTrace).ToList();

		return new McpOrderIdempotencyCall(
			requestNumber,
			behavior,
			idempotencyKey,
			confirmed,
			quantity,
			isError,
			errorCode,
			errorText,
			order?.OrderId ?? Guid.Empty,
			order?.Quantity,
			clock.ElapsedMilliseconds,
			toolTrace,
			toolSpan is not null,
			related.Count(IsMediator),
			related.Count(IsNpgsql));
	}

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

	private sealed record McpOrderIdempotencyCall(
		int RequestNumber,
		string Behavior,
		string IdempotencyKey,
		bool Confirmed,
		int RequestedQuantity,
		bool IsError,
		string? ErrorCode,
		string? Error,
		Guid OrderId,
		int? Quantity,
		long ClientDurationMs,
		string? ToolTraceId,
		bool SawNewToolSpan,
		int MediatorSpanCount,
		int NpgsqlSpanCount);

	private sealed record McpOrderIdempotencyConfiguration(
		string Tool,
		string ConfirmedArgument,
		string IdempotencyKeyArgument,
		string Store,
		string CacheKey,
		string Command);

	private sealed record McpOrderIdempotencyObservations(
		bool UnconfirmedIsError,
		string? UnconfirmedErrorCode,
		bool UnconfirmedSawNewToolSpan,
		int UnconfirmedMediatorSpans,
		int UnconfirmedNpgsqlSpans,
		bool MissIsError,
		Guid MissOrderId,
		int? MissQuantity,
		bool MissSawNewToolSpan,
		int MissMediatorSpans,
		int MissNpgsqlSpans,
		bool ReplayIsError,
		Guid ReplayOrderId,
		int? ReplayQuantity,
		bool ReplaySawNewToolSpan,
		int ReplayMediatorSpans,
		int ReplayNpgsqlSpans,
		bool ReplaySameOrderIdAsMiss,
		bool MutatedIsError,
		Guid MutatedOrderId,
		int? MutatedQuantity,
		bool MutatedSawNewToolSpan,
		int MutatedMediatorSpans,
		int MutatedNpgsqlSpans,
		bool MutatedKeptOriginalQuantity,
		bool FreshIsError,
		Guid FreshOrderId,
		int? FreshQuantity,
		bool FreshSawNewToolSpan,
		int FreshMediatorSpans,
		int FreshNpgsqlSpans,
		bool FreshDifferentOrderId,
		IReadOnlyList<string> Notes);

	private sealed record McpOrderIdempotencyExperimentResult(
		string Name,
		DateTimeOffset StartedUtc,
		string GitSha,
		string Environment,
		McpOrderIdempotencyConfiguration Configuration,
		IReadOnlyList<McpOrderIdempotencyCall> Calls,
		McpOrderIdempotencyObservations Observations);
}
