using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FeatureFusion.Infrastructure.Seeding;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Mcp;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Protocol;

namespace IntegrationTests.Api;

/// <summary>
/// Proves Demo Commerce product detail shares one Mediator query across HTTP and MCP.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class CatalogProductHttpMcpConvergenceTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;

	public CatalogProductHttpMcpConvergenceTests(AspireFixture fixture)
	{
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	/// <summary>HTTP and MCP list the same Demo Commerce catalog page (shared Mediator query, not confirmation).</summary>
	[Fact]
	public async Task Http_and_mcp_list_catalog_products()
	{
		var httpResponse = await _http.GetAsync("/api/v1/catalog/products?page=1&pageSize=5");
		httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var mcpResult = await mcp.CallToolAsync(
			"catalog.products.list",
			new Dictionary<string, object?> { ["page"] = 1, ["pageSize"] = 5 });

		(mcpResult.IsError ?? false).Should().BeFalse();
		mcpResult.StructuredContent.Should().NotBeNull();
	}

	/// <summary>Flagship product fields match across HTTP GET and MCP <c>catalog.product.get</c>.</summary>
	[Fact]
	public async Task Http_and_mcp_return_same_flagship_product()
	{
		var httpResponse = await _http.GetAsync($"/api/v1/catalog/products/{DemoCommerceSeed.FlagshipSlug}");
		httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		var httpDetail = await httpResponse.Content.ReadFromJsonAsync<ProductDetail>(JsonOptions);
		httpDetail.Should().NotBeNull();
		httpDetail!.Slug.Should().Be(DemoCommerceSeed.FlagshipSlug);
		httpDetail.Sku.Should().Be(DemoCommerceSeed.FlagshipSku);

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var mcpResult = await mcp.CallToolAsync(
			"catalog.product.get",
			new Dictionary<string, object?> { ["slug"] = DemoCommerceSeed.FlagshipSlug });

		(mcpResult.IsError ?? false).Should().BeFalse();
		mcpResult.StructuredContent.Should().NotBeNull();
		var mcpJson = mcpResult.StructuredContent!.Value.GetRawText();
		var mcpDetail = JsonSerializer.Deserialize<ProductDetail>(mcpJson, JsonOptions);
		mcpDetail.Should().NotBeNull();

		mcpDetail!.Id.Should().Be(httpDetail.Id);
		mcpDetail.Slug.Should().Be(httpDetail.Slug);
		mcpDetail.Sku.Should().Be(httpDetail.Sku);
		mcpDetail.Price.Should().Be(httpDetail.Price);
		mcpDetail.BrandSlug.Should().Be(httpDetail.BrandSlug);
		mcpDetail.CategorySlug.Should().Be(httpDetail.CategorySlug);
		mcpDetail.Images.Should().HaveCount(httpDetail.Images.Count);
		mcpDetail.Specifications.Should().HaveCount(httpDetail.Specifications.Count);
	}

	/// <summary>Unknown slug is a tool error on MCP (same domain outcome as HTTP 404, different envelope).</summary>
	[Fact]
	public async Task Mcp_unknown_slug_is_error()
	{
		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var result = await mcp.CallToolAsync(
			"catalog.product.get",
			new Dictionary<string, object?> { ["slug"] = "does-not-exist" });

		result.IsError.Should().BeTrue();
		var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Match(t =>
			t.Contains("not found", StringComparison.OrdinalIgnoreCase)
			|| t.Contains("404", StringComparison.OrdinalIgnoreCase));
	}

	private sealed record ProductDetail(
		int Id,
		string Name,
		string Slug,
		string Sku,
		decimal Price,
		string BrandSlug,
		string CategorySlug,
		List<object> Images,
		List<object> Specifications);
}
