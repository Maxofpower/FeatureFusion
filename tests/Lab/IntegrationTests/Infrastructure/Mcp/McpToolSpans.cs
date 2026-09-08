using IntegrationTests.Infrastructure.Telemetry;

namespace IntegrationTests.Infrastructure.Mcp;

/// <summary>
/// Picks the mcp.tool span for this invocation. Ignores earlier spans in the same
/// <see cref="InProcessActivityCapture"/> (full-suite listeners can retain extra stops).
/// </summary>
public static class McpToolSpans
{
	public static CapturedActivity? TakeNew(
		IReadOnlyList<CapturedActivity> all,
		string toolName,
		HashSet<string> seenTraceIds,
		DateTime startedUtc)
	{
		var floor = startedUtc.AddSeconds(-1);
		return all.FirstOrDefault(span =>
			span.DisplayName == "mcp.tool"
			&& span.StartTimeUtc >= floor
			&& span.Tags.TryGetValue("mcp.tool.name", out var name)
			&& name == toolName
			&& seenTraceIds.Add(span.TraceId));
	}
}
