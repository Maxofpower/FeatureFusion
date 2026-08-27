using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Telemetry.Internal.Pipeline;

/// <summary>
/// Logs the <see cref="TelemetryStartupSummary"/> once the host starts.
/// </summary>
/// <remarks>
/// Silence it with a log filter on this category.
/// </remarks>
internal sealed class TelemetryStartupSummaryReporter : IHostedService
{
    private readonly TelemetryStartupSummary _summary;
    private readonly ILogger<TelemetryStartupSummaryReporter> _logger;

    public TelemetryStartupSummaryReporter(
        TelemetryStartupSummary summary,
        ILogger<TelemetryStartupSummaryReporter> logger)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _summary.Write(_logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
