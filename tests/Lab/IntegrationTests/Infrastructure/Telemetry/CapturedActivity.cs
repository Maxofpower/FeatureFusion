using System.Diagnostics;

namespace IntegrationTests.Infrastructure.Telemetry;

/// <summary>
/// Immutable snapshot of a stopped <see cref="Activity"/> for in-process test observation.
/// </summary>
public sealed record CapturedActivity(
	string TraceId,
	string SpanId,
	string? ParentSpanId,
	string Source,
	string DisplayName,
	TimeSpan Duration,
	IReadOnlyDictionary<string, string> Tags,
	IReadOnlyList<ActivityLinkSnapshot> Links)
{
	public static CapturedActivity From(Activity activity)
	{
		var tags = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var tag in activity.Tags)
		{
			if (tag.Value is not null)
				tags[tag.Key] = tag.Value;
		}

		var links = activity.Links
			.Select(link => new ActivityLinkSnapshot(
				link.Context.TraceId.ToHexString(),
				link.Context.SpanId.ToHexString()))
			.ToList();

		return new CapturedActivity(
			activity.TraceId.ToHexString(),
			activity.SpanId.ToHexString(),
			activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
			activity.Source.Name,
			activity.DisplayName,
			activity.Duration,
			tags,
			links);
	}
}

/// <summary>Snapshot of an <see cref="ActivityLink"/> context for correlation experiments.</summary>
public sealed record ActivityLinkSnapshot(string TraceId, string SpanId);
