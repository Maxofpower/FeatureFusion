using System.Text.Json;
using BuildingBlocks.Mcp;
using BuildingBlocks.Mcp.Catalog;
using BuildingBlocks.Mcp.Protocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace BuildingBlocks.Mcp.Tests;

public sealed class ProtocolRegistrationTests
{
	[Theory]
	[InlineData("catalog://tools")]
	[InlineData("catalog://tools/")]
	[InlineData("Catalog://Tools")]
	public void Catalog_Uri_Accepts_Trailing_Slash_And_Case(string uri)
		=> Assert.True(McpProtocolRegistration.IsCatalogResourceUri(uri));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("catalog://other")]
	public void Catalog_Uri_Rejects_Unknown(string? uri)
		=> Assert.False(McpProtocolRegistration.IsCatalogResourceUri(uri));

	[Fact]
	public void ToTool_Schema_Has_Enums_Optional_And_Descriptions()
	{
		var d = McpToolScanner.FromType(typeof(SchemaProbe), null);
		var tool = McpProtocolRegistration.ToTool(d);
		var json = tool.InputSchema.GetRawText();

		Assert.Equal("tests.schema", tool.Name);
		Assert.Contains("Ascending", json, StringComparison.Ordinal);
		Assert.Contains("Descending", json, StringComparison.Ordinal);
		Assert.Contains("Pagination cursor", json, StringComparison.Ordinal);

		using var doc = JsonDocument.Parse(json);
		var required = doc.RootElement.GetProperty("required");
		var names = required.EnumerateArray().Select(e => e.GetString()).ToArray();
		Assert.Contains("requiredName", names);
		Assert.Contains("qty", names);
		Assert.DoesNotContain("cursor", names);
		Assert.DoesNotContain("limit", names);
		Assert.DoesNotContain("named", names);
	}

	[Fact]
	public void ToTool_Idempotent_Command_Advertises_Uuid_Key()
	{
		var d = McpToolScanner.FromType(
			typeof(CreateListedOrder),
			(_, _, _, _) => Task.FromResult(McpResult.Ok<object?>(1)));
		var json = McpProtocolRegistration.ToTool(d).InputSchema.GetRawText();
		Assert.Contains("idempotencyKey", json, StringComparison.Ordinal);
		Assert.Contains("\"format\":\"uuid\"", json, StringComparison.Ordinal);
		Assert.Contains("reuse the same value", json, StringComparison.OrdinalIgnoreCase);

		using var doc = JsonDocument.Parse(json);
		var key = doc.RootElement.GetProperty("properties").GetProperty("idempotencyKey");
		Assert.Equal("string", key.GetProperty("type").GetString());
		Assert.Equal(McpDefaults.IdempotencyKeyJsonFormat, key.GetProperty("format").GetString());
		Assert.Contains("idempotencyKey", doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
	}

	[Fact]
	public void Success_String_Payload_Wraps_StructuredContent_As_Object()
	{
		var result = McpProtocolRegistration.ToSuccessCallResult("pong:Ada");
		Assert.Equal("pong:Ada", Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
		Assert.Equal(JsonValueKind.Object, result.StructuredContent?.ValueKind);
		Assert.Equal("pong:Ada", result.StructuredContent!.Value.GetProperty("value").GetString());
	}

	[Fact]
	public void Success_Object_Payload_Keeps_StructuredContent_As_Object()
	{
		var result = McpProtocolRegistration.ToSuccessCallResult(new { echo = "hello-mcp" });
		Assert.Contains("hello-mcp", Assert.Single(result.Content.OfType<TextContentBlock>()).Text, StringComparison.Ordinal);
		Assert.Equal("hello-mcp", result.StructuredContent!.Value.GetProperty("echo").GetString());
	}

	[Fact]
	public void ToTool_Query_Omits_Idempotency_Key()
	{
		var d = McpToolScanner.FromType(typeof(ListedOrder), null);
		var json = McpProtocolRegistration.ToTool(d).InputSchema.GetRawText();
		Assert.DoesNotContain("idempotencyKey", json, StringComparison.Ordinal);
	}
}
