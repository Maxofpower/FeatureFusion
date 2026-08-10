using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Mediator.Telemetry;

/// <summary>
/// Optional Send enrichment (Activity + logging + exception observation).
/// Wraps the full pipeline + handler — not registered as an <see cref="IPipelineBehavior{TRequest,TResponse}"/>.
/// </summary>
public sealed class MediatorSendTelemetry
{
	private readonly ActivitySource _activitySource;
	private readonly MediatorTelemetryOptions _options;
	private readonly ILogger _logger;

	/// <summary>Creates Send telemetry using configured options.</summary>
	public MediatorSendTelemetry(
		IOptions<MediatorTelemetryOptions> options,
		ILoggerFactory loggerFactory)
	{
		_options = options.Value;
		_activitySource = new ActivitySource(_options.ActivitySourceName);
		_logger = loggerFactory.CreateLogger("BuildingBlocks.Mediator.Telemetry");
	}

	/// <summary>Activity source name (for host <c>AddSource</c>).</summary>
	public string ActivitySourceName => _activitySource.Name;

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
}
