using System.Diagnostics;

using System.Net.Http.Json;

using System.Text.Json;

using BuildingBlocks.Telemetry;

using FluentAssertions;

using IntegrationTests.Aspire;

using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Telemetry;

using Microsoft.AspNetCore.Mvc.Testing;

using ModelContextProtocol.Client;

using ModelContextProtocol.Protocol;

using static IntegrationTests.Infrastructure.Telemetry.LabTrace;



namespace IntegrationTests.Api;



/// <summary>

/// Phase 1 evidence-path proof: the Aspire fixture hosts FeatureFusion in-process

/// (<see cref="AspireFixture"/> is a <see cref="WebApplicationFactory{TEntryPoint}"/>).

/// Postgres/Redis/RabbitMQ/Memcached run in Aspire containers. An

/// <see cref="ActivityListener"/> in the test process therefore sees application

/// Activities — no OTLP collector required for this proof.

/// </summary>

[Collection(AspireCollection.Name)]

public sealed class FeatureFusionTraceEvidenceTests

{

	private static readonly JsonSerializerOptions JsonOptions = new()

	{

		PropertyNameCaseInsensitive = true

	};



	private readonly HttpClient _http;



	public FeatureFusionTraceEvidenceTests(AspireFixture fixture)

	{

		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions

		{

			AllowAutoRedirect = false

		});

	}



	[Fact]

	public async Task Http_products_page_produces_trace_evidence()

	{

		using var capture = new InProcessActivityCapture();

		var (traceId, spanId) = NewTraceParent();

		const int pageSize = 5;



		using var request = new HttpRequestMessage(

			HttpMethod.Get,

			$"/api/v2/products-page?limit={pageSize}&sortBy=Id&sortDirection=Ascending");

		request.Headers.TryAddWithoutValidation("traceparent", FormatTraceParent(traceId, spanId));



		var started = Stopwatch.StartNew();

		var response = await _http.SendAsync(request);

		started.Stop();



		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

		var page = await response.Content.ReadFromJsonAsync<ProductsPage>(JsonOptions);

		page.Should().NotBeNull();

		page!.Items.Should().HaveCount(pageSize);

		page.NextCursor.Should().NotBeNullOrWhiteSpace();

		page.Items.Select(i => i.Id).Should().OnlyHaveUniqueItems();



		var spans = capture.ForTrace(traceId);

		spans.Should().NotBeEmpty(

			"injected traceparent {0} should correlate ASP.NET Core → Mediator → Npgsql in the same process. Captured traces: {1}",

			traceId,

			DescribeTraces(capture));



		var httpSpans = spans.Where(IsAspNetCore).ToList();

		var mediatorSpans = spans.Where(IsMediator).ToList();

		var dbSpans = spans.Where(IsDatabase).ToList();



		httpSpans.Should().NotBeEmpty("ASP.NET Core instrumentation should emit the incoming products-page span");

		httpSpans.Should().Contain(

			s => s.DisplayName.Contains("products-page", StringComparison.OrdinalIgnoreCase)

				|| HasTag(s, "url.path", "/api/v2/products-page")

				|| HasTagContaining(s, "http.route", "products-page"),

			"incoming HTTP span should identify /api/v2/products-page. Spans: {0}",

			Describe(spans));



		mediatorSpans.Should().Contain(

			s => s.DisplayName.Contains("GetProductsQuery", StringComparison.Ordinal),

			"Mediator UseTelemetry should emit GetProductsQuery Handling. Spans: {0}",

			Describe(spans));

		mediatorSpans.Should().Contain(s => HasTag(s, "mediator.request_name", "GetProductsQuery"));

		mediatorSpans.Should().Contain(s => HasTag(s, TelemetryComponentTags.TagName, TelemetryComponentTags.Mediator));



		dbSpans.Should().NotBeEmpty(

			"Npgsql and/or EF Core instrumentation should emit at least one database span. Spans: {0}",

			Describe(spans));



		started.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);

		httpSpans.Concat(mediatorSpans).Should().Contain(s => s.Duration > TimeSpan.Zero);



		// Pagination evidence lives on the HTTP payload today (not Activity tags).

		page.Items.Count.Should().Be(pageSize);

		page.HasMore.Should().BeTrue();

		traceId.ToHexString().Should().HaveLength(32);

	}



	[Fact]

	public async Task Mcp_products_list_produces_trace_evidence()

	{

		using var capture = new InProcessActivityCapture();

		var (traceId, spanId) = NewTraceParent();

		_http.DefaultRequestHeaders.Remove("traceparent");

		_http.DefaultRequestHeaders.TryAddWithoutValidation("traceparent", FormatTraceParent(traceId, spanId));



		await using var mcp = await LabMcpClient.CreateAsync(_http);

		var result = await mcp.CallToolAsync(

			"products.list",

			new Dictionary<string, object?>

			{

				["limit"] = 5,

				["sortBy"] = "Id",

				["sortDirection"] = "Ascending"

			});



		(result.IsError ?? false).Should().BeFalse(GetText(result));

		result.StructuredContent.Should().NotBeNull();

		var page = ParseProductsPage(result.StructuredContent!.Value);

		page.Items.Should().HaveCount(5);

		page.NextCursor.Should().NotBeNullOrWhiteSpace();

		page.Items.Select(i => i.Id).Should().OnlyHaveUniqueItems();



		// Incoming /mcp continues the injected traceparent. Tool invoke may run

		// without Activity.Current (Streamable HTTP session), so also search the

		// in-process sink by source rather than assuming one TraceId.

		var httpSpans = capture.ForTrace(traceId);

		httpSpans.Where(IsAspNetCore).Should().NotBeEmpty(

			"MCP Streamable HTTP is still an incoming ASP.NET Core request on trace {0}. Captured: {1}",

			traceId,

			DescribeTraces(capture));



		var mcpSpan = capture.All.Should().ContainSingle(

			s => s.DisplayName == "mcp.tool" && HasTag(s, "mcp.tool.name", "products.list"),

			"MCP UseTelemetry should emit mcp.tool with mcp.tool.name=products.list. Captured: {0}",

			Describe(capture.All)).Which;

		mcpSpan.Tags.Should().ContainKey(TelemetryComponentTags.TagName)

			.WhoseValue.Should().Be(TelemetryComponentTags.Mcp);



		var mediatorSpan = capture.All.Should().ContainSingle(

			s => IsMediator(s) && s.DisplayName.Contains("GetProductsQuery", StringComparison.Ordinal),

			"products.list should dispatch GetProductsQuery through Mediator. Captured: {0}",

			Describe(capture.All)).Which;



		var dbSpans = capture.All.Where(s => s.TraceId == mediatorSpan.TraceId).Where(IsDatabase).ToList();

		dbSpans.Should().NotBeEmpty(

			"GetProductsQuery should hit PostgreSQL on the same trace as Mediator ({0}). Captured: {1}",

			mediatorSpan.TraceId,

			Describe(capture.All.Where(s => s.TraceId == mediatorSpan.TraceId).ToList()));



		mcpSpan.TraceId.Should().Be(

			mediatorSpan.TraceId,

			"McpInvoker starts mcp.tool then ISender.Send; those Activities share Activity.Current");

	}



	private static bool IsDatabase(CapturedActivity span)

		=> span.Source is "Npgsql" or "OpenTelemetry.Instrumentation.EntityFrameworkCore"

			|| HasTag(span, TelemetryComponentTags.TagName, TelemetryComponentTags.Npgsql)

			|| HasTag(span, TelemetryComponentTags.TagName, TelemetryComponentTags.EntityFrameworkCore);



	private static string Describe(IReadOnlyList<CapturedActivity> spans)

		=> string.Join(" | ", spans.Select(s => $"{s.Source}:{s.DisplayName}"));



	private static string DescribeTraces(InProcessActivityCapture capture)

	{

		var groups = capture.All

			.GroupBy(s => s.TraceId)

			.Select(g => $"{g.Key}({string.Join(",", g.Select(s => s.Source).Distinct())})");

		return string.Join("; ", groups);

	}



	private static string GetText(CallToolResult result)

		=> string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));



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



	private sealed record ProductsPage(

		IReadOnlyList<ProductItem> Items,

		string NextCursor,

		string PreviousCursor,

		bool HasMore,

		bool HasPrevious,

		int TotalCount);



	private sealed record ProductItem(int Id, string Name, decimal Price, DateTime CreatedAt);

}


