namespace FeatureFusion.Features.Admission;

/// <summary>Lab-only options for capability admission (not a policy engine).</summary>
public sealed class CapabilityAdmissionOptions
{
	public const string SectionName = "CapabilityAdmission";

	/// <summary>
	/// Capability ids that must Defer before <c>ISender.Send</c>.
	/// Empty = Allow for all (existing Experiment / smoke behavior).
	/// </summary>
	public List<string> DeferredCapabilities { get; set; } = [];

	/// <summary>How long a Pending ticket may be released.</summary>
	public TimeSpan TicketTtl { get; set; } = TimeSpan.FromHours(1);

	public bool IsDeferred(string capabilityId) =>
		DeferredCapabilities.Any(c => string.Equals(c, capabilityId, StringComparison.Ordinal));
}
