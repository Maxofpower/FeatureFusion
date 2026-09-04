using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Mcp;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.McpToolStormRateLimit;

/// <summary>
/// Experiment 16 — MCP tool-storm bounded by <see cref="IMcpRateLimiter"/>.
/// Hypothesis: a burst of <c>orders.create</c> calls with <b>distinct</b> idempotency keys
/// cannot be collapsed by the idempotency store; a configured limiter rejects surplus calls
/// with <see cref="McpErrorCode.RateLimited"/> before Mediator/Npgsql/business execution.
/// This is not an idempotency experiment. Test-host limiter only (Lab default remains
/// <see cref="NoOpRateLimiter"/>). Does not change package rate-limiter code or add a NuGet.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpToolStormRateLimitExperimentTests
{
	private const string ToolName = "orders.create";
	private const int ProductId = 1;
	private const int CustomerId = 1;
	private const int Quantity = 2;
	private const int MaxPermits = 2;
	private const int StormSize = 5;
	private const int RetryAfterSeconds = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly WebApplicationFactory<Program> _factory;
	private readonly HttpClient _http;
	private readonly FixedPermitMcpRateLimiter _limiter;
	private readonly ITestOutputHelper _output;

	public McpToolStormRateLimitExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;

		_limiter = new FixedPermitMcpRateLimiter(
			toolName: ToolName,
			maxPermits: MaxPermits,
			retryAfterSeconds: RetryAfterSeconds);

		_factory = fixture.WithWebHostBuilder(builder =>
		{
			builder.ConfigureTestServices(services =>
			{
				services.RemoveAll<IMcpRateLimiter>();
				services.AddSingleton<IMcpRateLimiter>(_limiter);
			});
		});

		_http = _factory.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Distinct_key_mcp_write_storm_is_bounded_by_rate_limiter_before_production()
	{
		_fixture.ProcessedEvents.Clear();
		_limiter.Reset();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var (transportTraceId, transportSpanId) = NewTraceParent();
		_http.DefaultRequestHeaders.Remove("traceparent");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"traceparent",
			FormatTraceParent(transportTraceId, transportSpanId));

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var seenToolTraces = new HashSet<string>(StringComparer.Ordinal);
		var calls = new List<StormCall>();

		for (var i = 0; i < StormSize; i++)
		{
			var key = System.Ulid.NewUlid().ToString();
			await CallAsync(
				mcp,
				capture,
				seenToolTraces,
				calls,
				behavior: $"Storm_{i + 1}_DistinctKey",
				idempotencyKey: key);
		}

		var accepted = calls.Where(c => !c.IsError).ToList();
		var rateLimited = calls.Where(c =>
			c.IsError && string.Equals(c.ErrorCode, nameof(McpErrorCode.RateLimited), StringComparison.Ordinal)).ToList();
		var otherErrors = calls.Where(c => c.IsError && c.ErrorCode != nameof(McpErrorCode.RateLimited)).ToList();

		var acceptedOrderIds = accepted
			.Where(c => c.OrderId != Guid.Empty)
			.Select(c => c.OrderId)
			.Distinct()
			.ToList();

		var outboxByOrder = new Dictionary<Guid, int>();
		foreach (var orderId in acceptedOrderIds)
			outboxByOrder[orderId] = (await OrderOutboxObserver.FindByOrderIdAsync(_factory.Services, orderId)).Count;

		var mcpToolSpansForOrdersCreate = capture.All.Count(s =>
			s.DisplayName == "mcp.tool" && HasToolTag(s, ToolName));

		var hypothesisConfirmed =
			accepted.Count == MaxPermits
			&& rateLimited.Count == StormSize - MaxPermits
			&& otherErrors.Count == 0
			&& accepted.All(c => c.SawNewToolSpan && c.MediatorSpanCount > 0 && c.NpgsqlSpanCount > 0)
			&& rateLimited.All(c =>
				!c.SawNewToolSpan
				&& c.MediatorSpanCount == 0
				&& c.NpgsqlSpanCount == 0
				&& c.OrderId == Guid.Empty)
			&& acceptedOrderIds.Count == MaxPermits
			&& mcpToolSpansForOrdersCreate == MaxPermits;

		var result = new
		{
			name = "mcp-orders-create-tool-storm-rate-limit-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			environment = "Development",
			configuration = new
			{
				tool = ToolName,
				stormSize = StormSize,
				maxPermits = MaxPermits,
				retryAfterSeconds = RetryAfterSeconds,
				limiter = "Test-only FixedPermitMcpRateLimiter (per toolName, fixed permit count); Lab default remains NoOpRateLimiter",
				idempotencyNote = "Distinct Ulid keys per call so MemoryIdempotencyStore cannot collapse the storm",
				pipelineNote = "McpInvoker checks IMcpRateLimiter after visibility and before confirmation/idempotency/InvokeCore (mcp.tool Activity + dispatcher)"
			},
			calls,
			observations = new
			{
				attempted = calls.Count,
				accepted = accepted.Count,
				rateLimited = rateLimited.Count,
				otherErrors = otherErrors.Count,
				limiterAcquireAttempts = _limiter.AcquireAttempts,
				distinctAcceptedOrderIds = acceptedOrderIds.Count,
				outboxRowsByOrderId = outboxByOrder,
				mcpToolSpansForOrdersCreate,
				acceptedMediatorTotal = accepted.Sum(c => c.MediatorSpanCount),
				acceptedNpgsqlTotal = accepted.Sum(c => c.NpgsqlSpanCount),
				rateLimitedMediatorTotal = rateLimited.Sum(c => c.MediatorSpanCount),
				rateLimitedNpgsqlTotal = rateLimited.Sum(c => c.NpgsqlSpanCount),
				hypothesisConfirmed,
				hypothesis = "Distinct-key MCP write storm is bounded by IMcpRateLimiter before Mediator/Npgsql/business execution."
			},
			notes = new[]
			{
				"Distinct idempotency keys ensure the storm is not suppressed by MCP idempotency (contrast Exp 13 amplification without a limiter).",
				"Package semantics: IMcpRateLimiter receives toolName + McpInvokeContext; scope is implementation-defined. This experiment uses a fixed per-tool permit budget.",
				"Rejected calls must not emit mcp.tool (Activity starts only in InvokeCore) and must not reach Mediator/Npgsql.",
				hypothesisConfirmed
					? "Hypothesis CONFIRMED: limiter accepted MaxPermits production writes and RateLimited the surplus before production."
					: "Hypothesis FALSIFIED or inconclusive relative to packaged rate-limit boundary."
			}
		};

		_output.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

		calls.Should().HaveCount(StormSize);
		otherErrors.Should().BeEmpty(
			"storm outcomes should be success or RateLimited only. Unexpected: {0}",
			string.Join("; ", otherErrors.Select(c => $"{c.Behavior}:{c.ErrorCode}:{c.Error}")));

		accepted.Should().HaveCount(MaxPermits,
			"fixed permit limiter should allow exactly {0} orders.create calls", MaxPermits);
		rateLimited.Should().HaveCount(StormSize - MaxPermits);

		foreach (var call in accepted)
		{
			call.ErrorCode.Should().BeNull();
			call.OrderId.Should().NotBeEmpty();
			call.SawNewToolSpan.Should().BeTrue(
				"accepted call must enter InvokeCore and emit mcp.tool. {0}", call.Behavior);
			call.MediatorSpanCount.Should().BeGreaterThan(0, call.Behavior);
			call.NpgsqlSpanCount.Should().BeGreaterThan(0, call.Behavior);
		}

		foreach (var call in rateLimited)
		{
			call.ErrorCode.Should().Be(nameof(McpErrorCode.RateLimited), call.Behavior);
			call.OrderId.Should().BeEmpty(call.Behavior);
			call.SawNewToolSpan.Should().BeFalse(
				"RateLimited must return before InvokeCore (no mcp.tool). {0} error={1}",
				call.Behavior, call.Error);
			call.MediatorSpanCount.Should().Be(0, call.Behavior);
			call.NpgsqlSpanCount.Should().Be(0, call.Behavior);
			call.RetryAfterSeconds.Should().Be(RetryAfterSeconds);
		}

		acceptedOrderIds.Should().HaveCount(MaxPermits);
		mcpToolSpansForOrdersCreate.Should().Be(MaxPermits);
		_limiter.AcquireAttempts.Should().Be(StormSize);

		foreach (var orderId in acceptedOrderIds)
		{
			outboxByOrder[orderId].Should().BeGreaterThanOrEqualTo(1,
				"each accepted production order should leave an outbox row. orderId={0}", orderId);
		}

		rateLimited.Sum(c => c.MediatorSpanCount).Should().Be(0);
		rateLimited.Sum(c => c.NpgsqlSpanCount).Should().Be(0);
	}

	private async Task<StormCall> CallAsync(
		McpClient mcp,
		InProcessActivityCapture capture,
		HashSet<string> seenToolTraces,
		List<StormCall> calls,
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
		var errorText = isError ? McpToolResults.Truncate(McpToolResults.GetText(result), 240) : null;
		var errorCode = isError ? TryReadErrorCode(result) : null;
		var retryAfter = isError ? TryReadRetryAfterSeconds(result) : null;
		var order = !isError ? McpToolResults.TryParseOrder(result, JsonOptions) : null;

		var toolSpan = capture.All.FirstOrDefault(s =>
			s.DisplayName == "mcp.tool"
			&& HasToolTag(s, ToolName)
			&& seenToolTraces.Add(s.TraceId));

		var toolTrace = toolSpan?.TraceId;
		var related = toolTrace is null
			? (IReadOnlyList<CapturedActivity>)Array.Empty<CapturedActivity>()
			: capture.All.Where(s => s.TraceId == toolTrace).ToList();

		var call = new StormCall(
			RequestNumber: calls.Count + 1,
			Behavior: behavior,
			IdempotencyKey: idempotencyKey,
			IsError: isError,
			ErrorCode: errorCode,
			Error: errorText,
			RetryAfterSeconds: retryAfter,
			OrderId: order?.OrderId ?? Guid.Empty,
			ClientDurationMs: clock.ElapsedMilliseconds,
			ToolTraceId: toolTrace,
			SawNewToolSpan: toolSpan is not null,
			MediatorSpanCount: related.Count(IsMediator),
			NpgsqlSpanCount: related.Count(IsNpgsql));

		calls.Add(call);
		return call;
	}

	private static string? TryReadErrorCode(CallToolResult result)
	{
		var code = McpToolResults.TryReadJsonErrorCode(result);
		if (code is not null)
			return code;

		var text = McpToolResults.GetText(result);
		return text.Contains("RateLimited", StringComparison.OrdinalIgnoreCase)
			? nameof(McpErrorCode.RateLimited)
			: null;
	}

	private static int? TryReadRetryAfterSeconds(CallToolResult result)
	{
		var text = McpToolResults.GetText(result);
		if (string.IsNullOrWhiteSpace(text))
			return null;

		try
		{
			using var doc = JsonDocument.Parse(text);
			if (doc.RootElement.TryGetProperty("retryAfterSeconds", out var retry)
				&& retry.ValueKind == JsonValueKind.Number
				&& retry.TryGetInt32(out var seconds))
			{
				return seconds;
			}
		}
		catch (JsonException)
		{
			// ignore
		}

		return null;
	}

	private static bool HasToolTag(CapturedActivity span, string toolName)
		=> span.Tags.Any(t =>
			t.Key == "mcp.tool.name"
			&& string.Equals(t.Value, toolName, StringComparison.Ordinal));

	/// <summary>
	/// Test-only fixed-budget limiter. Allows the first <see cref="MaxPermits"/> acquires for
	/// one tool name; other tools always allow. Not production code.
	/// </summary>
	public sealed class FixedPermitMcpRateLimiter : IMcpRateLimiter
	{
		private readonly string _toolName;
		private readonly int _maxPermits;
		private readonly int? _retryAfterSeconds;
		private int _acquireAttempts;

		public FixedPermitMcpRateLimiter(string toolName, int maxPermits, int? retryAfterSeconds)
		{
			_toolName = toolName;
			_maxPermits = maxPermits;
			_retryAfterSeconds = retryAfterSeconds;
		}

		public int MaxPermits => _maxPermits;
		public int AcquireAttempts => Volatile.Read(ref _acquireAttempts);

		public void Reset() => Volatile.Write(ref _acquireAttempts, 0);

		public ValueTask<McpRateLimitDecision> TryAcquireAsync(
			string toolName,
			McpInvokeContext context,
			CancellationToken cancellationToken)
		{
			if (!string.Equals(toolName, _toolName, StringComparison.OrdinalIgnoreCase))
				return ValueTask.FromResult(McpRateLimitDecision.Allow);

			var attempt = Interlocked.Increment(ref _acquireAttempts);
			if (attempt <= _maxPermits)
				return ValueTask.FromResult(McpRateLimitDecision.Allow);

			return ValueTask.FromResult(McpRateLimitDecision.Deny(_retryAfterSeconds));
		}
	}

	private sealed record StormCall(
		int RequestNumber,
		string Behavior,
		string IdempotencyKey,
		bool IsError,
		string? ErrorCode,
		string? Error,
		int? RetryAfterSeconds,
		Guid OrderId,
		long ClientDurationMs,
		string? ToolTraceId,
		bool SawNewToolSpan,
		int MediatorSpanCount,
		int NpgsqlSpanCount);

}
