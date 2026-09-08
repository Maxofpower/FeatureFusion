using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Payments;

public sealed record PaymentRecordId : EntityId<int>
{
	public PaymentRecordId(int value) : base(value)
	{
	}

	public static PaymentRecordId From(int value) => new(value);
	public static implicit operator PaymentRecordId(int value) => new(value);
	public static explicit operator int(PaymentRecordId id) => id.Value;
}

public enum PaymentOutcome
{
	Approved = 1,
	Declined = 2
}

/// <summary>Non-sensitive payment attempt recorded after checkout authorization.</summary>
public class PaymentRecord : Entity<PaymentRecordId>
{
	public Guid CorrelationOrderId { get; private set; }
	public int? DomainOrderId { get; private set; }
	public PaymentOutcome Outcome { get; private set; }
	public decimal Amount { get; private set; }
	public string Currency { get; private set; } = "EUR";
	public DateTime CreatedAtUtc { get; private set; }

	private PaymentRecord()
	{
	}

	public static PaymentRecord Create(
		Guid correlationOrderId,
		PaymentOutcome outcome,
		decimal amount,
		DateTime createdAtUtc,
		int? domainOrderId = null,
		string currency = "EUR")
	{
		if (correlationOrderId == Guid.Empty)
			throw new DomainException("Correlation order id is required.");
		if (amount < 0)
			throw new DomainException("Payment amount cannot be negative.");
		if (createdAtUtc.Kind != DateTimeKind.Utc)
			throw new DomainException("CreatedAt must be UTC.");

		return new PaymentRecord
		{
			CorrelationOrderId = correlationOrderId,
			DomainOrderId = domainOrderId,
			Outcome = outcome,
			Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
			Currency = currency.Trim().ToUpperInvariant(),
			CreatedAtUtc = createdAtUtc
		};
	}

	public void AttachDomainOrder(int domainOrderId) => DomainOrderId = domainOrderId;
}
