using System.Diagnostics;
using BuildingBlocks.Telemetry;

namespace IntegrationTests.Infrastructure.Telemetry;

/// <summary>
/// Generic W3C trace injection and common Activity source/tag predicates for Lab tests.
/// Callers interpret captured evidence; this type does not assert experiment semantics.
/// </summary>
public static class LabTrace
{
	public static (ActivityTraceId TraceId, ActivitySpanId SpanId) NewTraceParent()
		=> (ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());

	public static string FormatTraceParent(ActivityTraceId traceId, ActivitySpanId spanId)
		=> $"00-{traceId}-{spanId}-01";

	public static bool IsAspNetCore(CapturedActivity span)
		=> span.Source == "Microsoft.AspNetCore"
			|| HasTag(span, TelemetryComponentTags.TagName, TelemetryComponentTags.AspNetCore);

	public static bool IsMediator(CapturedActivity span)
		=> span.Source == "BuildingBlocks.Mediator"
			|| HasTag(span, TelemetryComponentTags.TagName, TelemetryComponentTags.Mediator);

	public static bool IsNpgsql(CapturedActivity span)
		=> span.Source == "Npgsql";

	public static bool HasTag(CapturedActivity span, string key, string expected)
		=> span.Tags.TryGetValue(key, out var value)
			&& string.Equals(value, expected, StringComparison.Ordinal);

	public static bool HasTagContaining(CapturedActivity span, string key, string fragment)
		=> span.Tags.TryGetValue(key, out var value)
			&& value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
