using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Mcp;
using EventBusRabbitMQ.Infrastructure;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Agent;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Reporting;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.MafMcpPrototype;

/// <summary>
/// MAF prototype spike (not Exp 15): a real Microsoft Agent Framework agent in front of the existing
/// FeatureFusion <c>/mcp</c> endpoint. Characterizes whether MAF can drive observable MCP tool sequences
/// against the production Lab application without changing BuildingBlocks.Mcp.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class MafMcpPrototypeTests
{
	private const string Goal =
		"Create an order for product 1, quantity 2, for customer 1.";

	private const string AgentInstructions =
		"""
		You are a lab agent that completes tasks using only the MCP tools provided.
		When a write tool requires idempotencyKey, generate a new UUID/ULID for each distinct operation.
		When a write tool requires confirmed=true, set it before invoking the tool.
		Prefer the smallest number of tool calls needed to complete the goal.
		""";

	private const string OrdersCreateTool = "orders.create";
	private const string OrderCreatedEventType = nameof(OrderCreatedIntegrationEvent);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly IServiceProvider _services;
	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public MafMcpPrototypeTests(AspireFixture fixture, ITestOutputHelper output)
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
	public async Task Maf_mcp_transport_connects_and_lists_featurefusion_tools()
	{
		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var tools = await mcp.ListToolsAsync();
		var names = tools.Select(t => t.Name).ToArray();

		names.Should().Contain(["demo.echo", "products.list", OrdersCreateTool, "lab.ping"]);
	}

	[Fact]
	public async Task Maf_agent_runs_goal_and_records_observed_tool_sequence()
	{
		if (!TryCreateChatClient(out var chatClient, out var provider, out var model, out var skipReason))
		{
			_output.WriteLine(skipReason);
			return;
		}

		const int runCount = 3;
		_fixture.ProcessedEvents.Clear();

		var startedUtc = DateTimeOffset.UtcNow;
		using var capture = new InProcessActivityCapture();
		var (transportTraceId, transportSpanId) = NewTraceParent();
		_http.DefaultRequestHeaders.Remove("traceparent");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"traceparent",
			FormatTraceParent(transportTraceId, transportSpanId));

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var mcpTools = await mcp.ListToolsAsync();

		var agent = chatClient.AsAIAgent(
			name: "FeatureFusionLabAgent",
			instructions: AgentInstructions,
			tools: [.. mcpTools.Cast<AITool>()]);

		var runs = new List<MafPrototypeRun>();
		for (var i = 0; i < runCount; i++)
		{
			capture.Clear();
			var runStarted = DateTimeOffset.UtcNow;
			var runStopwatch = Stopwatch.StartNew();
			var session = await agent.CreateSessionAsync();
			using var runCts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
			var response = await agent.RunAsync(Goal, session, cancellationToken: runCts.Token);
			runStopwatch.Stop();

			var toolCalls = MafAgentRunJournal.ReadToolCalls(response);
			var sequence = toolCalls.Select(c => c.ToolName).ToArray();
			var orderId = await TryResolveOrderIdAsync(toolCalls, response.Text);
			var outboxRows = orderId == Guid.Empty
				? []
				: await WaitForOutboxRowsAsync(orderId);

			if (orderId != Guid.Empty)
			{
				await Wait.UntilAsync(
					() => _fixture.ProcessedEvents.Any(e => e.OrderId == orderId),
					TimeSpan.FromSeconds(20));
			}

			var mcpToolSpans = capture.All
				.Where(s => s.DisplayName == "mcp.tool")
				.ToList();

			var ordersCreateSpans = mcpToolSpans
				.Where(s => HasToolTag(s, OrdersCreateTool))
				.ToList();

			runs.Add(new MafPrototypeRun(
				RunNumber: i + 1,
				StartedUtc: runStarted,
				DurationMs: runStopwatch.ElapsedMilliseconds,
				FinalText: response.Text,
				ToolCallCount: toolCalls.Count,
				ToolSequence: sequence,
				ToolCalls: toolCalls,
				OrderId: orderId == Guid.Empty ? null : orderId,
				BusinessOperationSucceeded: orderId != Guid.Empty,
				OutboxRowCount: outboxRows.Count,
				ProcessedEventCount: orderId == Guid.Empty
					? 0
					: _fixture.ProcessedEvents.Count(e => e.OrderId == orderId),
				McpToolSpanCount: mcpToolSpans.Count,
				OrdersCreateSpanCount: ordersCreateSpans.Count,
				MediatorSpanCount: capture.All.Count(IsMediator),
				NpgsqlSpanCount: capture.All.Count(IsNpgsql)));
		}

		var artifact = new
		{
			name = "maf-mcp-prototype-v1",
			startedUtc,
			gitSha = LabRunInfo.ReadGitSha(),
			environment = "Development",
			configuration = new
			{
				provider,
				model,
				goal = Goal,
				systemInstructions = AgentInstructions,
				runCount,
				mcpEndpoint = "/mcp",
				transportTraceId = transportTraceId.ToHexString()
			},
			runs,
			observations = new
			{
				Sequences = runs.Select(r => r.ToolSequence).ToArray(),
				AllRunsSucceeded = runs.All(r => r.BusinessOperationSucceeded),
				SequenceVariance = DescribeSequenceVariance(runs)
			},
			notes = new[]
			{
				"Prototype spike only — not a numbered Lab experiment.",
				"BuildingBlocks.Mcp and production application code are unchanged.",
				"Agent chooses tool order; sequences are recorded as observed."
			}
		};

		_output.WriteLine(JsonSerializer.Serialize(artifact, JsonOptions));

		runs.Should().NotBeEmpty("at least one MAF run should be recorded when a chat provider is configured");
		runs.Should().OnlyContain(
			r => r.ToolCallCount > 0,
			"each MAF run should invoke at least one MCP tool");

		// Characterize outcome; do not assert a specific tool order.
		var succeeded = runs.Count(r => r.BusinessOperationSucceeded);
		_output.WriteLine($"MAF prototype: {succeeded}/{runs.Count} runs produced an order.");
	}

	private static bool TryCreateChatClient(
		out ChatClient chatClient,
		out string provider,
		out string model,
		out string skipReason)
	{
		chatClient = null!;
		provider = "";
		model = "";
		skipReason = "";

		var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
		if (!string.IsNullOrWhiteSpace(openAiKey))
		{
			model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
			provider = "OpenAI";
			chatClient = new OpenAIClient(openAiKey).GetChatClient(model);
			return true;
		}

		var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
		var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
		if (!string.IsNullOrWhiteSpace(azureEndpoint) && !string.IsNullOrWhiteSpace(azureKey))
		{
			model = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
			provider = "AzureOpenAI";
			chatClient = new OpenAIClient(
				new ApiKeyCredential(azureKey),
				new OpenAIClientOptions { Endpoint = new Uri(azureEndpoint) })
				.GetChatClient(model);
			return true;
		}

		var ollamaBase = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
		var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
		if (!string.IsNullOrWhiteSpace(ollamaBase) && !string.IsNullOrWhiteSpace(ollamaModel))
		{
			model = ollamaModel;
			provider = "Ollama";
			chatClient = new OpenAIClient(
				new ApiKeyCredential("ollama"),
				new OpenAIClientOptions { Endpoint = ToOpenAiCompatibleEndpoint(ollamaBase) })
				.GetChatClient(model);
			return true;
		}

		skipReason =
			"SKIP: Set OPENAI_API_KEY, AZURE_OPENAI_ENDPOINT + AZURE_OPENAI_API_KEY, or OLLAMA_BASE_URL + OLLAMA_MODEL for live MAF agent runs.";
		return false;
	}

	private static Uri ToOpenAiCompatibleEndpoint(string baseUrl)
	{
		var trimmed = baseUrl.TrimEnd('/');
		if (!trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
			trimmed += "/v1";

		return new Uri(trimmed);
	}

	private static string DescribeSequenceVariance(IReadOnlyList<MafPrototypeRun> runs)
	{
		if (runs.Count <= 1)
			return "single-run";

		var first = string.Join("→", runs[0].ToolSequence);
		if (runs.All(r => string.Join("→", r.ToolSequence) == first))
			return "identical-sequences";

		var sameLength = runs.Select(r => r.ToolSequence.Count).Distinct().Count() == 1;
		return sameLength ? "partially-different-sequences" : "substantially-different-sequences";
	}

	private async Task<Guid> TryResolveOrderIdAsync(
		IReadOnlyList<MafObservedToolCall> toolCalls,
		string? finalText)
	{
		foreach (var call in toolCalls.Where(c => c.ToolName == OrdersCreateTool).Reverse())
		{
			var fromResult = TryParseOrderId(call.ResultJson);
			if (fromResult != Guid.Empty)
				return fromResult;
		}

		return TryParseOrderId(finalText);
	}

	private static Guid TryParseOrderId(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return Guid.Empty;

		try
		{
			using var doc = JsonDocument.Parse(text);
			if (TryReadOrderId(doc.RootElement, out var orderId))
				return orderId;
		}
		catch (JsonException)
		{
			// fall through to regex
		}

		const string guidPattern =
			@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";
		var match = System.Text.RegularExpressions.Regex.Match(text, guidPattern);
		return match.Success && Guid.TryParse(match.Value, out var parsed) ? parsed : Guid.Empty;
	}

	private static bool TryReadOrderId(JsonElement element, out Guid orderId)
	{
		orderId = Guid.Empty;
		if (element.ValueKind == JsonValueKind.Object)
		{
			if (element.TryGetProperty("orderId", out var direct)
				&& direct.TryGetGuid(out orderId))
				return true;

			if (element.TryGetProperty("value", out var value)
				&& TryReadOrderId(value, out orderId))
				return true;
		}

		return false;
	}

	private async Task<IReadOnlyList<OutboxRowObservation>> WaitForOutboxRowsAsync(Guid orderId)
	{
		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < TimeSpan.FromSeconds(20))
		{
			var rows = await FindOutboxRowsForOrderIdAsync(orderId);
			if (rows.Count > 0)
				return rows;

			await Task.Delay(100);
		}

		return [];
	}

	private async Task<IReadOnlyList<OutboxRowObservation>> FindOutboxRowsForOrderIdAsync(Guid orderId)
	{
		await using var scope = _services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var serializerOptions = scope.ServiceProvider
			.GetRequiredService<IOptions<EventBusSubscriptionInfo>>().Value.JsonSerializerOptions;

		var rows = await db.OutboxMessages
			.AsNoTracking()
			.Where(m => m.EventType == OrderCreatedEventType)
			.ToListAsync();

		var matches = new List<OutboxRowObservation>();
		foreach (var row in rows)
		{
			if (row.Payload is null)
				continue;

			var evt = JsonSerializer.Deserialize<OrderCreatedIntegrationEvent>(
				row.Payload,
				serializerOptions);

			if (evt is null || evt.OrderId != orderId)
				continue;

			matches.Add(new OutboxRowObservation(
				row.Id,
				evt.Id,
				evt.OrderId,
				row.Status.ToString()));
		}

		return matches;
	}

	private static bool HasToolTag(CapturedActivity span, string toolName) =>
		span.Tags.TryGetValue("mcp.tool.name", out var name) && name == toolName;

	private sealed record OutboxRowObservation(
		Guid OutboxMessageId,
		Guid IntegrationEventId,
		Guid OrderId,
		string Status);

	private sealed record MafPrototypeRun(
		int RunNumber,
		DateTimeOffset StartedUtc,
		long DurationMs,
		string? FinalText,
		int ToolCallCount,
		IReadOnlyList<string> ToolSequence,
		IReadOnlyList<MafObservedToolCall> ToolCalls,
		Guid? OrderId,
		bool BusinessOperationSucceeded,
		int OutboxRowCount,
		int ProcessedEventCount,
		int McpToolSpanCount,
		int OrdersCreateSpanCount,
		int MediatorSpanCount,
		int NpgsqlSpanCount);
}
