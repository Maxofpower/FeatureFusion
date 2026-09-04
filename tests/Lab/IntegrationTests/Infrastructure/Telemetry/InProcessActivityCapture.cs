using System.Collections.Concurrent;
using System.Diagnostics;

namespace IntegrationTests.Infrastructure.Telemetry;

/// <summary>
/// Captures stopped Activities from the test process via <see cref="ActivityListener"/>.
/// Used when FeatureFusion runs in-process through <c>WebApplicationFactory</c>.
/// </summary>
public sealed class InProcessActivityCapture : IDisposable
{
	private readonly ActivityListener _listener;
	private readonly ConcurrentBag<CapturedActivity> _stopped = [];

	public InProcessActivityCapture()
	{
		_listener = new ActivityListener
		{
			ShouldListenTo = static _ => true,
			Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
				ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => _stopped.Add(CapturedActivity.From(activity))
		};
		ActivitySource.AddActivityListener(_listener);
	}

	public IReadOnlyList<CapturedActivity> All => _stopped.ToList();

	public IReadOnlyList<CapturedActivity> ForTrace(ActivityTraceId traceId)
		=> ForTraceHex(traceId.ToHexString());

	public IReadOnlyList<CapturedActivity> ForTraceHex(string traceIdHex)
		=> _stopped.Where(s => s.TraceId == traceIdHex).ToList();

	/// <summary>
	/// Drops spans captured so far. Use between independent runs that share one listener
	/// so per-run counts are not cumulative.
	/// </summary>
	public void Clear() => _stopped.Clear();

	public void Dispose() => _listener.Dispose();
}
