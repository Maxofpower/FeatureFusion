namespace FeatureFusion.Features.Admission;

/// <summary>
/// Application-owned capability admission. Lives in front of <c>ISender.Send</c>.
/// Surfaces (HTTP, MCP) call this; they do not own deferral policy.
/// </summary>
public interface ICapabilityAdmission
{
	Task<AdmissionDecision> AdmitAsync(
		string capabilityId,
		string requestKey,
		string intentPayloadJson,
		string intentHash,
		CancellationToken cancellationToken);

	Task<AdmissionReleaseResult> ReleaseAsync(
		Guid ticketId,
		string? releasedBy,
		CancellationToken cancellationToken);
}
