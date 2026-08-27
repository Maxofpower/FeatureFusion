using System.Diagnostics;
using BuildingBlocks.Telemetry;
using Xunit;

namespace BuildingBlocks.Telemetry.Tests.Spans;

public sealed class TelemetryActivityTests
{
    [Fact]
    public void Start_returns_null_when_no_listener()
    {
        using var activity = TelemetryActivity.Start("BuildingBlocks.Telemetry.Tests.NoListener", "op");
        Assert.Null(activity);
    }

    [Fact]
    public void Start_creates_activity_when_listener_is_present()
    {
        using var listener = CreateListener("BuildingBlocks.Telemetry.Tests.Start");
        using var activity = TelemetryActivity.Start("BuildingBlocks.Telemetry.Tests.Start", "Checkout");

        Assert.NotNull(activity);
        Assert.Equal("Checkout", activity!.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
    }

    [Fact]
    public void AddEvent_appends_named_event_with_tags()
    {
        using var listener = CreateListener("BuildingBlocks.Telemetry.Tests.Events");
        using var activity = TelemetryActivity.Start("BuildingBlocks.Telemetry.Tests.Events", "Checkout");
        Assert.NotNull(activity);

        activity.AddEvent("payment.started", new KeyValuePair<string, object?>("order.id", 7));

        var ev = Assert.Single(activity!.Events);
        Assert.Equal("payment.started", ev.Name);
        Assert.Contains(ev.Tags, t => t.Key == "order.id" && Equals(t.Value, 7));
    }

    [Fact]
    public void AddEvent_is_noop_when_activity_is_null()
    {
        Activity? activity = null;
        Assert.Null(activity.AddEvent("ignored"));
    }

    [Fact]
    public void RecordException_sets_error_status()
    {
        using var listener = CreateListener("BuildingBlocks.Telemetry.Tests.Exception");
        using var activity = TelemetryActivity.Start("BuildingBlocks.Telemetry.Tests.Exception", "Checkout");
        Assert.NotNull(activity);

        activity.RecordException(new InvalidOperationException("boom"));

        Assert.Equal(ActivityStatusCode.Error, activity!.Status);
        Assert.Equal("boom", activity.StatusDescription);
    }

    [Fact]
    public void RecordException_is_noop_when_activity_is_null()
    {
        Activity? activity = null;
        Assert.Null(activity.RecordException(new InvalidOperationException("boom")));
    }

    private static ActivityListener CreateListener(string sourceName)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
