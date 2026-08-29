using System.Text.Json;
using BuildingBlocks.Mcp;
using FluentAssertions;
using IntegrationTests.Aspire;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace IntegrationTests.Api;

/// <summary>
/// Live Streamable HTTP MCP against FeatureFusion <c>/mcp</c> (same Aspire + WAF fixture as API smoke).
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class FeatureFusionMcpTests
{
	private readonly HttpClient _http;

	public FeatureFusionMcpTests(AspireFixture fixture)
	{
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Tools_List_Contains_Opt_In_Tools_Only()
	{
		await using var mcp = await CreateClientAsync();
		var tools = await mcp.ListToolsAsync();
		var names = tools.Select(t => t.Name).ToArray();

		names.Should().Contain(["demo.echo", "products.list", "orders.create", "lab.ping"]);
		names.Should().NotContain(n => n.Contains("void", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Call_Demo_Echo_Succeeds()
	{
		await using var mcp = await CreateClientAsync();
		var result = await mcp.CallToolAsync(
			"demo.echo",
			new Dictionary<string, object?> { ["message"] = "hello-mcp" });

		(result.IsError ?? false).Should().BeFalse();
		var text = GetText(result);
		text.Should().Contain("hello-mcp");
	}

	[Fact]
	public async Task Call_Orders_Create_Without_Confirm_And_Key_Is_Error()
	{
		await using var mcp = await CreateClientAsync();
		var result = await mcp.CallToolAsync(
			"orders.create",
			new Dictionary<string, object?>
			{
				["productId"] = 1,
				["quantity"] = 1,
				["customerId"] = 1
			});

		result.IsError.Should().BeTrue();
		var text = GetText(result);
		text.Should().Match(t =>
			t.Contains("ConfirmationRequired", StringComparison.OrdinalIgnoreCase)
			|| t.Contains("IdempotencyKeyRequired", StringComparison.OrdinalIgnoreCase)
			|| t.Contains("confirmed", StringComparison.OrdinalIgnoreCase)
			|| t.Contains("idempotency", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Call_Orders_Create_With_Confirm_And_Key_Succeeds()
	{
		await using var mcp = await CreateClientAsync();
		var result = await mcp.CallToolAsync(
			"orders.create",
			new Dictionary<string, object?>
			{
				["productId"] = 1,
				["quantity"] = 1,
				["customerId"] = 1,
				["confirmed"] = true,
				["idempotencyKey"] = Ulid.NewUlid().ToString()
			});

		(result.IsError ?? false).Should().BeFalse();
		var text = GetText(result);
		text.Should().Match(t =>
			t.Contains("order", StringComparison.OrdinalIgnoreCase)
			|| t.Contains("success", StringComparison.OrdinalIgnoreCase)
			|| t.Contains("id", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Products_List_Schema_Has_Enums_And_Optional_Cursor()
	{
		await using var mcp = await CreateClientAsync();
		var tools = await mcp.ListToolsAsync();
		var products = tools.Should().ContainSingle(t => t.Name == "products.list").Subject;
		var schema = products.ProtocolTool.InputSchema;
		var json = schema.GetRawText();

		json.Should().Contain("Ascending");
		json.Should().Contain("Descending");
		json.Should().Contain("Maximum number of items to return");

		if (schema.TryGetProperty("required", out var required))
		{
			var names = required.EnumerateArray().Select(e => e.GetString()).ToArray();
			names.Should().NotContain("cursor");
			names.Should().NotContain("limit");
			names.Should().NotContain("sortBy");
			names.Should().NotContain("sortDirection");
		}
	}

	[Fact]
	public async Task Call_Demo_Echo_Includes_StructuredContent()
	{
		await using var mcp = await CreateClientAsync();
		var result = await mcp.CallToolAsync(
			"demo.echo",
			new Dictionary<string, object?> { ["message"] = "hello-mcp" });

		(result.IsError ?? false).Should().BeFalse();
		GetText(result).Should().Contain("hello-mcp");
		result.StructuredContent.Should().NotBeNull();
		result.StructuredContent!.Value.GetRawText().Should().Contain("hello-mcp");
	}

	[Fact]
	public async Task Catalog_Resource_Lists_Lab_Tools()
	{
		await using var mcp = await CreateClientAsync();
		var read = await mcp.ReadResourceAsync(new Uri(McpDefaults.CatalogResourceUri));
		var markdown = string.Join("\n", read.Contents.OfType<TextResourceContents>().Select(c => c.Text));
		markdown.Should().Contain("demo.echo");
		markdown.Should().Contain("products.list");
		markdown.Should().Contain("orders.create");
		markdown.Should().Contain("lab.ping");
	}

	[Fact]
	public async Task Call_Lab_Ping_From_Minimal_Api_Method_Succeeds()
	{
		await using var mcp = await CreateClientAsync();
		var result = await mcp.CallToolAsync(
			"lab.ping",
			new Dictionary<string, object?> { ["name"] = "Ada" });

		(result.IsError ?? false).Should().BeFalse();
		GetText(result).Should().Contain("pong:Ada");
		result.StructuredContent.Should().NotBeNull();
	}

	private async Task<McpClient> CreateClientAsync()
	{
		var endpoint = new Uri(_http.BaseAddress ?? new Uri("http://localhost"), "mcp");
		var transport = new HttpClientTransport(
			new HttpClientTransportOptions { Endpoint = endpoint },
			_http,
			ownsHttpClient: false);
		return await McpClient.CreateAsync(transport);
	}

	private static string GetText(CallToolResult result)
	{
		return string.Join(
			"\n",
			result.Content.OfType<TextContentBlock>().Select(b => b.Text));
	}
}
