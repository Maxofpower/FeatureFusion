namespace FeatureFusion.Features.Admission;

public enum IntentTicketStatus
{
	Pending = 0,
	Released = 1,
	Expired = 2
}

/// <summary>
/// Durable deferred execution intent — not a workflow instance or approval platform record.
/// </summary>
public sealed class IntentTicket
{
	public Guid Id { get; set; }

	public string CapabilityId { get; set; } = "";

	/// <summary>Caller request key (HTTP Idempotency-Key / MCP idempotencyKey).</summary>
	public string RequestKey { get; set; } = "";

	/// <summary>Canonical intent fingerprint for same-key payload conflict detection.</summary>
	public string IntentHash { get; set; } = "";

	/// <summary>JSON payload used to reconstruct the command on release.</summary>
	public string IntentPayload { get; set; } = "";

	public IntentTicketStatus Status { get; set; }

	public DateTimeOffset CreatedAt { get; set; }

	public DateTimeOffset ExpiresAt { get; set; }

	public DateTimeOffset? ReleasedAt { get; set; }

	public string? ReleasedBy { get; set; }

	/// <summary>
	/// Correlation id produced after a successful release (observation only).
	/// For orders.create this is the EventBus/HTTP <c>OrderResponse.OrderId</c> Guid — not the EF int PK.
	/// </summary>
	public Guid? ExecutionOrderId { get; set; }
}
