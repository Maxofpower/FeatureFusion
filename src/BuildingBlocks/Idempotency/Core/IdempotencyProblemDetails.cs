using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.Idempotency;

/// <summary>
/// Builds RFC 9457 <see cref="ProblemDetails"/> for idempotency filter/gate errors.
/// </summary>
/// <remarks>
/// Stable <c>type</c> URIs use the <c>https://buildingblocks.dev/errors/idempotency/</c> namespace.
/// Successful replays are not ProblemDetails — hosts return the cached envelope.
/// </remarks>
public static class IdempotencyProblemDetails
{
	/// <summary>Base URI for machine-readable problem types.</summary>
	public const string TypeBase = "https://buildingblocks.dev/errors/idempotency/";

	/// <summary>Creates ProblemDetails for a non-success gate decision.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="decision"/> or <paramref name="options"/> is null.</exception>
	/// <exception cref="ArgumentException"><paramref name="decision"/> is <see cref="IdempotencyDecisionKind.Execute"/> or <see cref="IdempotencyDecisionKind.Replay"/>.</exception>
	public static ProblemDetails For(IdempotencyDecision decision, IdempotencyOptions options)
	{
		ArgumentNullException.ThrowIfNull(decision);
		ArgumentNullException.ThrowIfNull(options);

		return decision.Kind switch
		{
			IdempotencyDecisionKind.BadKey => Create(
				StatusCodes.Status400BadRequest,
				"key-invalid",
				"Idempotency-Key invalid",
				decision.Detail ?? "The Idempotency-Key header is missing or invalid.",
				IdempotencyOutcomes.BadKey),

			IdempotencyDecisionKind.Unauthorized => Create(
				StatusCodes.Status401Unauthorized,
				"unauthorized",
				"Idempotency caller identity required",
				decision.Detail ?? "User ID is missing in the request context.",
				IdempotencyOutcomes.Unauthorized),

			IdempotencyDecisionKind.ProcessingConflict => Create(
				options.ProcessingConflictStatusCode,
				"processing",
				"Request already in progress",
				decision.Detail ?? "Request is already being processed. Please wait.",
				IdempotencyOutcomes.ProcessingConflict),

			IdempotencyDecisionKind.FingerprintConflict => Create(
				options.FingerprintConflictStatusCode,
				"fingerprint-mismatch",
				"Idempotency-Key payload mismatch",
				decision.Detail ?? "Idempotency-Key was reused with a different request payload.",
				IdempotencyOutcomes.FingerprintConflict),

			IdempotencyDecisionKind.DuplicateConflict => Create(
				options.DuplicateConflictStatusCode,
				"duplicate",
				"Idempotency-Key already completed",
				decision.Detail ?? "Idempotency-Key was already used for a completed request.",
				IdempotencyOutcomes.DuplicateConflict),

			IdempotencyDecisionKind.LockFailure => Create(
				StatusCodes.Status500InternalServerError,
				"lock-failure",
				"Idempotency infrastructure failure",
				decision.Detail ?? "Idempotency infrastructure failure.",
				IdempotencyOutcomes.LockFailure),

			_ => throw new ArgumentException(
				$"Decision kind {decision.Kind} does not map to ProblemDetails.",
				nameof(decision))
		};
	}

	private static ProblemDetails Create(
		int status,
		string typeSuffix,
		string title,
		string detail,
		string outcome)
	{
		var problem = new ProblemDetails
		{
			Status = status,
			Type = TypeBase + typeSuffix,
			Title = title,
			Detail = detail
		};
		problem.Extensions["idempotency.outcome"] = outcome;
		return problem;
	}
}
