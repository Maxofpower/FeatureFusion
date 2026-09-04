using System.Text.Json;
using BuildingBlocks.Mcp;
using ModelContextProtocol.Protocol;

namespace IntegrationTests.Infrastructure.Mcp;

/// <summary>
/// Lab-only parsing of MCP <see cref="CallToolResult"/> payloads.
/// Observation only — no assertions; experiment-specific error fallbacks stay in tests.
/// </summary>
public static class McpToolResults
{
	private static readonly JsonSerializerOptions DefaultJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public static string GetText(CallToolResult result)
		=> string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));

	public static string Truncate(string text, int maxLength = 500)
		=> text.Length <= maxLength ? text : text[..maxLength];

	/// <summary>
	/// Parses <c>orders.create</c> structured content (root or wrapped <c>value</c>).
	/// </summary>
	public static McpOrderBody? TryParseOrder(
		CallToolResult result,
		JsonSerializerOptions? jsonOptions = null)
	{
		if (result.StructuredContent is not { } structured)
			return null;

		var options = jsonOptions ?? DefaultJsonOptions;
		var order = JsonSerializer.Deserialize<McpOrderBody>(structured.GetRawText(), options);
		if (order?.OrderId != Guid.Empty)
			return order;

		if (structured.ValueKind == JsonValueKind.Object
			&& structured.TryGetProperty("value", out var value))
		{
			order = JsonSerializer.Deserialize<McpOrderBody>(value.GetRawText(), options);
			if (order?.OrderId != Guid.Empty)
				return order;
		}

		return null;
	}

	/// <summary>
	/// Reads a JSON <c>code</c> property from tool error text when present.
	/// Does not apply experiment-specific string heuristics (ConfirmationRequired / RateLimited).
	/// </summary>
	public static string? TryReadJsonErrorCode(CallToolResult result)
	{
		var text = GetText(result);
		if (string.IsNullOrWhiteSpace(text))
			return null;

		try
		{
			using var doc = JsonDocument.Parse(text);
			if (!doc.RootElement.TryGetProperty("code", out var code))
				return null;

			return code.ValueKind switch
			{
				JsonValueKind.String => code.GetString(),
				JsonValueKind.Number => Enum.GetName(typeof(McpErrorCode), code.GetInt32()),
				_ => code.GetRawText()
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}
}

/// <summary>Shared shape for MCP <c>orders.create</c> structured content.</summary>
public sealed record McpOrderBody(Guid OrderId, int Quantity, decimal? TotalAmount);
