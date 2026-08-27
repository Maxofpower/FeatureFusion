using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Mediator.Telemetry;

/// <summary>
/// Optional Send enrichment (Activity + logging + exception observation + opt-in metrics).
/// Wraps the full pipeline + handler — not registered as an <see cref="IPipelineBehavior{TRequest,TResponse}"/>.
/// </summary>
public sealed class MediatorSendTelemetry : IDisposable
{
	private readonly ActivitySource _activitySource;
	private readonly MediatorTelemetryOptions _options;
	private readonly ILogger _logger;
	private readonly Meter? _meter;
	private readonly Histogram<double>? _duration;
	private readonly Counter<long>? _send;
	private bool _disposed;

	/// <summary>Creates Send telemetry using configured options.</summary>
	public MediatorSendTelemetry(
		IOptions<MediatorTelemetryOptions> options,
		ILoggerFactory loggerFactory)
	{
		_options = options.Value;
		_activitySource = new ActivitySource(_options.ActivitySourceName);
		_logger = loggerFactory.CreateLogger("BuildingBlocks.Mediator.Telemetry");

		if (_options.EnableMetrics)
		{
			var meterName = string.IsNullOrWhiteSpace(_options.MeterName)
				? _options.ActivitySourceName
				: _options.MeterName;
			_meter = new Meter(meterName);
			_duration = _meter.CreateHistogram<double>("mediator.send.duration", unit: "ms");
			_send = _meter.CreateCounter<long>("mediator.send");
		}
	}

	/// <summary>Activity source name (for host <c>AddSource</c>).</summary>
	public string ActivitySourceName => _activitySource.Name;

	/// <summary>Meter name when metrics are enabled; otherwise <see langword="null"/>.</summary>
	public string? MeterName => _meter?.Name;

	/// <summary>The underlying <see cref="ActivitySource"/>.</summary>
	public ActivitySource Source => _activitySource;

	/// <summary>Runs <paramref name="send"/> inside an Activity when listeners are present.</summary>
	/// <param name="messageType">Concrete command/query type.</param>
	/// <param name="messageKind">Tag value: <c>command</c>, <c>query</c>, or <c>void-command</c>.</param>
	/// <param name="send">Pipeline + handler continuation.</param>
	/// <param name="cancellationToken">Cancellation token for the Send.</param>
	public async Task<TResponse> TraceAsync<TResponse>(
		Type messageType,
		string messageKind,
		Func<CancellationToken, Task<TResponse>> send,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArgumentNullException.ThrowIfNull(messageType);
		ArgumentNullException.ThrowIfNull(messageKind);
		ArgumentNullException.ThrowIfNull(send);

		var requestName = messageType.Name;
		var stopwatch = Stopwatch.StartNew();

		using var activity = _activitySource.StartActivity($"{requestName} Handling", ActivityKind.Internal);
		activity?.SetTag("mediator.request_name", requestName);
		activity?.SetTag("mediator.request_type", messageType.FullName);
		activity?.SetTag("mediator.message_kind", messageKind);

		if (_options.EnableLogging)
			_logger.LogInformation("Handling {MessageKind} {RequestType}", messageKind, requestName);

		try
		{
			var response = await send(cancellationToken).ConfigureAwait(false);
			stopwatch.Stop();

			activity?.SetTag("mediator.success", true);
			activity?.SetTag("mediator.duration_ms", stopwatch.ElapsedMilliseconds);

			RecordMetrics(requestName, messageKind, success: true, stopwatch.Elapsed.TotalMilliseconds);

			if (_options.EnableLogging)
			{
				_logger.LogInformation(
					"Handled {MessageKind} {RequestType} in {ElapsedMs} ms",
					messageKind,
					requestName,
					stopwatch.ElapsedMilliseconds);
			}

			return response;
		}
		catch (Exception ex)
		{
			stopwatch.Stop();
			activity?.SetTag("mediator.success", false);
			activity?.SetTag("mediator.duration_ms", stopwatch.ElapsedMilliseconds);
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

			RecordMetrics(requestName, messageKind, success: false, stopwatch.Elapsed.TotalMilliseconds);

			if (_options.RecordException)
			{
#if NET9_0_OR_GREATER
				activity?.AddException(ex);
#endif
				activity?.SetTag("exception.type", ex.GetType().FullName);
				activity?.SetTag("exception.message", ex.Message);
			}

			if (_options.EnableLogging)
			{
				_logger.LogError(
					ex,
					"Error handling {MessageKind} {RequestType} after {ElapsedMs} ms",
					messageKind,
					requestName,
					stopwatch.ElapsedMilliseconds);
			}

			throw;
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		_meter?.Dispose();
		_activitySource.Dispose();
	}

	private void RecordMetrics(string requestName, string messageKind, bool success, double elapsedMs)
	{
		if (_duration is null || _send is null)
			return;

		var durationTags = new TagList
		{
			{ "mediator.message_kind", messageKind },
			{ "mediator.request_name", requestName },
		};
		_duration.Record(elapsedMs, durationTags);

		var countTags = new TagList
		{
			{ "mediator.message_kind", messageKind },
			{ "mediator.request_name", requestName },
			{ "mediator.success", success },
		};
		_send.Add(1, countTags);
	}
}
