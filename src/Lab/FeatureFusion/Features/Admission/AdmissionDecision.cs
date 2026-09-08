namespace FeatureFusion.Features.Admission;

/// <summary>Outcome of <see cref="ICapabilityAdmission.AdmitAsync"/>.</summary>
public abstract record AdmissionDecision
{
	private AdmissionDecision() { }

	public sealed record Allow : AdmissionDecision;

	public sealed record Deny(string Error, int StatusCode) : AdmissionDecision;

	public sealed record Defer(AdmissionPendingResponse Pending) : AdmissionDecision;
}

/// <summary>Public pending payload returned to HTTP/MCP callers (no business effect yet).</summary>
public sealed record AdmissionPendingResponse(
	Guid TicketId,
	string CapabilityId,
	string Status,
	DateTimeOffset CreatedAt,
	DateTimeOffset ExpiresAt,
	string Outcome = "Pending");

public sealed record AdmissionReleaseResult(
	bool Succeeded,
	string? Error,
	int StatusCode,
	Guid? OrderId,
	Guid TicketId);
