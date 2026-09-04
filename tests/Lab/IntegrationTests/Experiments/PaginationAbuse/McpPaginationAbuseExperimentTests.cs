using System.Diagnostics;
using System.Text.Json;
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

namespace IntegrationTests.Experiments.PaginationAbuse;

/// <summary>
/// Experiment 2: same careless pagination behaviors through MCP <c>products.list</c>.
/// Compares the MCP envelope with HTTP Experiment 1. Does not re-prove keyset SQL.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpPaginationAbuseExperimentTests
{
	private const string ToolName = "products.list";
	private const int Limit = 7;
	private const int WalkPages = 8;
	private const int TamperDelta = 50;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public McpPaginationAbuseExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_output = output;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Cursor_abuse_against_mcp_products_list_is_observed()
	{
		using var capture = new InProcessActivityCapture();
		var (transportTraceId, transportSpanId) = NewTraceParent();
		_http.DefaultRequestHeaders.Remove("traceparent");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"traceparent",
			FormatTraceParent(transportTraceId, transportSpanId));

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var seenToolTraces = new HashSet<string>(StringComparer.Ordinal);
		var calls = new List<McpPaginationAbuseCall>();
		var requestNumber = 0;

		async Task<McpPaginationAbuseCall> CallAsync(string behavior, string cursor)
		{
			requestNumber++;
			var args = new Dictionary<string, object?>
			{
				["limit"] = Limit,
				["sortBy"] = "Id",
				["sortDirection"] = "Ascending",
				["pageDirection"] = "Forward"
			};
			if (!string.IsNullOrEmpty(cursor))
				args["cursor"] = cursor;

			var clock = Stopwatch.StartNew();
			var result = await mcp.CallToolAsync(ToolName, args);
			clock.Stop();

			var isError = result.IsError ?? false;
			var errorText = isError ? McpToolResults.Truncate(McpToolResults.GetText(result)) : null;
			var page = !isError && result.StructuredContent is { } structured
				? ParseProductsPage(structured)
				: null;

			var toolSpan = capture.All.FirstOrDefault(s =>
				s.DisplayName == "mcp.tool"
				&& HasTag(s, "mcp.tool.name", ToolName)
				&& seenToolTraces.Add(s.TraceId));

			var toolTrace = toolSpan?.TraceId;
			var related = toolTrace is null
				? []
				: capture.All.Where(s => s.TraceId == toolTrace).ToList();
			var mediator = related.Where(IsMediator).ToList();

			return new McpPaginationAbuseCall(
				requestNumber,
				behavior,
				cursor,
				isError,
				errorText,
				page?.Items.Select(i => i.Id).ToArray() ?? [],
				page?.NextCursor ?? "",
				page?.PreviousCursor ?? "",
				page?.HasMore ?? false,
				page?.HasPrevious ?? false,
				page?.TotalCount ?? 0,
				clock.ElapsedMilliseconds,
				transportTraceId.ToHexString(),
				toolTrace,
				toolSpan is not null,
				mediator.Count,
				mediator.Count == 0 ? null : mediator.Max(s => s.Duration.TotalMilliseconds),
				related.Count(IsNpgsql));
		}

		for (var i = 0; i < WalkPages; i++)
		{
			var cursorIn = i == 0 ? "" : calls[^1].NextCursor;
			calls.Add(await CallAsync("Walk", cursorIn));
		}

		var walks = calls.Where(c => c.Behavior == "Walk").ToList();
		var walkIds = walks.SelectMany(c => c.ItemIds).ToList();
		var walkUnique = walkIds.Distinct().ToList();

		var walk2 = walks[1];
		var replay = await CallAsync("Replay", walk2.CursorIn);
		calls.Add(replay);

		var forged = CarelessPaginationClient.TamperSeekId(walks[0].NextCursor, TamperDelta);
		var tamper = await CallAsync("Tamper", forged);
		calls.Add(tamper);

		var malformed = await CallAsync("MalformedProbe", "not-a-valid-cursor");
		calls.Add(malformed);

		var transportHttp = capture.ForTrace(transportTraceId).Where(IsAspNetCore).ToList();
		var firstToolTrace = walks[0].ToolTraceId;
		var toolVsTransport = firstToolTrace is not null
			&& !string.Equals(firstToolTrace, transportTraceId.ToHexString(), StringComparison.Ordinal);

		var payload = new
		{
			name = "mcp-products-list-cursor-abuse-v1",
			startedUtc = DateTimeOffset.UtcNow,
			gitSha = LabRunInfo.ReadGitSha(),
			tool = ToolName,
			transportTraceId = transportTraceId.ToHexString(),
			calls,
			observations = new
			{
				walkCalls = walks.Count,
				walkUniqueIds = walkUnique.Count,
				walkDuplicateIds = walkIds.Count - walkUnique.Count,
				replaySameIdsAsOriginal = replay.ItemIds.SequenceEqual(walk2.ItemIds),
				tamperIsError = tamper.IsError,
				tamperReturnedIds = tamper.ItemIds,
				malformedIsError = malformed.IsError,
				malformedError = malformed.Error,
				malformedMediatorSpanCount = malformed.MediatorSpanCount,
				malformedNpgsqlSpanCount = malformed.NpgsqlSpanCount,
				transportAspNetCoreSpanCount = transportHttp.Count,
				firstToolTraceId = firstToolTrace,
				toolTraceDistinctFromTransport = toolVsTransport,
				notes = new[]
				{
					$"Walk unique={walkUnique.Count}, duplicates={walkIds.Count - walkUnique.Count}.",
					$"Replay {(replay.ItemIds.SequenceEqual(walk2.ItemIds) ? "matched" : "did not match")} walk page 2.",
					$"Tamper isError={tamper.IsError}; ids=[{string.Join(",", tamper.ItemIds)}].",
					$"Malformed isError={malformed.IsError}; mediator={malformed.MediatorSpanCount}; npgsql={malformed.NpgsqlSpanCount}.",
					$"POST /mcp transport TraceId={transportTraceId.ToHexString()}; first mcp.tool TraceId={firstToolTrace ?? "(none)"}; distinct={toolVsTransport}."
				}
			}
		};
		_output.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

		foreach (var walk in walks)
		{
			walk.IsError.Should().BeFalse(walk.Error);
			walk.ItemIds.Should().HaveCount(Limit);
		}

		walkUnique.Should().HaveCount(56);
		(walkIds.Count - walkUnique.Count).Should().Be(0, "clean MCP walk should not overlap pages");
		walks[0].HasPrevious.Should().BeFalse();

		replay.IsError.Should().BeFalse(replay.Error);
		replay.ItemIds.Should().Equal(walk2.ItemIds);

		tamper.IsError.Should().BeFalse(tamper.Error);
		tamper.ItemIds.Should().Equal(58, 59, 60, 61, 62, 63, 64);

		malformed.IsError.Should().BeTrue("MCP should report a tool-level error for a malformed cursor");
		malformed.NpgsqlSpanCount.Should().Be(0, "malformed cursor should not query PostgreSQL");

		transportHttp.Should().NotBeEmpty("Streamable HTTP POST /mcp should continue the injected transport traceparent");
		firstToolTrace.Should().NotBeNullOrWhiteSpace();
		toolVsTransport.Should().BeTrue(
			"mcp.tool TraceId should not be the POST /mcp transport TraceId (Phase 1)");
		walks[0].SawMcpToolSpan.Should().BeTrue();
		walks[0].MediatorSpanCount.Should().BeGreaterThan(0);
		walks[0].NpgsqlSpanCount.Should().BeGreaterThan(0);
		walks[0].ToolTraceId.Should().NotBeNull();
		capture.All.Where(s => s.TraceId == walks[0].ToolTraceId && IsMediator(s))
			.Should().Contain(s => s.DisplayName.Contains("GetProductsQuery", StringComparison.Ordinal));
	}

	private static ProductsPage ParseProductsPage(JsonElement structured)
	{
		var json = structured.GetRawText();
		var page = JsonSerializer.Deserialize<ProductsPage>(json, JsonOptions);
		if (page?.Items is { Count: > 0 })
			return page;

		if (structured.ValueKind == JsonValueKind.Object && structured.TryGetProperty("value", out var value))
		{
			page = JsonSerializer.Deserialize<ProductsPage>(value.GetRawText(), JsonOptions);
			if (page?.Items is { Count: > 0 })
				return page;
		}

		throw new InvalidOperationException($"Could not parse products page from MCP structured content: {json}");
	}

	private sealed record McpPaginationAbuseCall(
		int RequestNumber,
		string Behavior,
		string CursorIn,
		bool IsError,
		string? Error,
		IReadOnlyList<int> ItemIds,
		string NextCursor,
		string PreviousCursor,
		bool HasMore,
		bool HasPrevious,
		int TotalCount,
		long ClientDurationMs,
		string TransportTraceId,
		string? ToolTraceId,
		bool SawMcpToolSpan,
		int MediatorSpanCount,
		double? MediatorDurationMs,
		int NpgsqlSpanCount);
}
