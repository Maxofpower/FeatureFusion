using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Idempotency;

/// <summary>Options for optional idempotency ActivitySource instrumentation.</summary>
public sealed class IdempotencyTelemetryOptions
{
	/// <summary>ActivitySource name. Default <c>BuildingBlocks.Idempotency</c>.</summary>
	public string ActivitySourceName { get; set; } = "BuildingBlocks.Idempotency";

	/// <summary>
	/// When true, tags activities with the full composite cache key (high cardinality / possible PII).
	/// Default false.
	/// </summary>
	public bool IncludeCacheKeyInTelemetry { get; set; }
}

/// <summary>
/// Optional ActivitySource for idempotency outcomes. Register via
/// <c>UseTelemetry</c> / <c>AddIdempotencyTelemetry</c>; omit for zero library telemetry overhead.
/// Hosts export with <c>AddSource("BuildingBlocks.Idempotency")</c> — no Telemetry package dependency.
/// </summary>
public sealed class IdempotencyTelemetry
{
	private readonly IdempotencyTelemetryOptions _options;

	/// <summary>Creates telemetry from options.</summary>
	public IdempotencyTelemetry(IdempotencyTelemetryOptions options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		var name = string.IsNullOrWhiteSpace(options.ActivitySourceName)
			? "BuildingBlocks.Idempotency"
			: options.ActivitySourceName;
		Source = new ActivitySource(name);
	}

	/// <summary>Activity source for short-lived idempotency outcome activities.</summary>
	public ActivitySource Source { get; }

	/// <summary>Starts a completed activity tagged with the outcome (no-op when no listeners).</summary>
	/// <param name="outcome">Value from <see cref="IdempotencyOutcomes"/>.</param>
	/// <param name="useLock">Whether the endpoint requested a distributed lock.</param>
	/// <param name="cacheKey">Composite cache key; exported only when <see cref="IdempotencyTelemetryOptions.IncludeCacheKeyInTelemetry"/> is true.</param>
	/// <param name="statusCode">Optional HTTP status associated with the decision.</param>
	public void Record(
		string outcome,
		bool useLock = false,
		string? cacheKey = null,
		int? statusCode = null)
	{
		using var activity = Source.StartActivity("idempotency", ActivityKind.Internal);
		if (activity is null)
			return;

		activity.SetTag("idempotency.outcome", outcome);
		activity.SetTag("idempotency.use_lock", useLock);
		if (statusCode is { } code)
			activity.SetTag("http.response.status_code", code);
		if (_options.IncludeCacheKeyInTelemetry && cacheKey is not null)
			activity.SetTag("idempotency.cache_key", cacheKey);
	}

	/// <summary>Records telemetry for a gate decision.</summary>
	public void RecordDecision(IdempotencyDecision decision, bool useLock, IdempotencyOptions options)
	{
		ArgumentNullException.ThrowIfNull(decision);
		ArgumentNullException.ThrowIfNull(options);

		var (outcome, status) = decision.Kind switch
		{
			IdempotencyDecisionKind.Execute => (IdempotencyOutcomes.Executed, (int?)null),
			IdempotencyDecisionKind.Replay => (IdempotencyOutcomes.Replayed, decision.Envelope?.StatusCode),
			IdempotencyDecisionKind.ProcessingConflict => (IdempotencyOutcomes.ProcessingConflict, options.ProcessingConflictStatusCode),
			IdempotencyDecisionKind.FingerprintConflict => (IdempotencyOutcomes.FingerprintConflict, options.FingerprintConflictStatusCode),
			IdempotencyDecisionKind.DuplicateConflict => (IdempotencyOutcomes.DuplicateConflict, options.DuplicateConflictStatusCode),
			IdempotencyDecisionKind.BadKey => (IdempotencyOutcomes.BadKey, StatusCodes.Status400BadRequest),
			IdempotencyDecisionKind.Unauthorized => (IdempotencyOutcomes.Unauthorized, StatusCodes.Status401Unauthorized),
			IdempotencyDecisionKind.LockFailure => (IdempotencyOutcomes.LockFailure, StatusCodes.Status500InternalServerError),
			_ => (decision.Kind.ToString().ToLowerInvariant(), null)
		};

		// Execute is recorded after successful Complete by the host.
		if (decision.Kind == IdempotencyDecisionKind.Execute)
			return;

		Record(outcome, useLock, decision.CacheKey, status);
	}
}
