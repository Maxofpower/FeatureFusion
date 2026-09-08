using BuildingBlocks.Domain;

namespace FeatureFusion.Domain.Customers;

/// <summary>Customer aggregate (identity for orders; not a storefront checkout surface).</summary>
public class Customer : AggregateRoot<CustomerId>
{
	/// <summary>Unique email.</summary>
	public Email Email { get; private set; } = null!;

	/// <summary>Display name.</summary>
	public string DisplayName { get; private set; } = "";

	/// <summary>UTC creation timestamp.</summary>
	public DateTime CreatedAt { get; private set; }

	private Customer()
	{
	}

	/// <summary>Creates a customer.</summary>
	public static Customer Create(Email email, string displayName, DateTime createdAtUtc, CustomerId? id = null)
	{
		ArgumentNullException.ThrowIfNull(email);
		if (string.IsNullOrWhiteSpace(displayName))
			throw new DomainException("Customer display name is required.");
		if (createdAtUtc.Kind != DateTimeKind.Utc)
			throw new DomainException("CreatedAt must be UTC.");

		var customer = new Customer
		{
			Email = email,
			DisplayName = displayName.Trim(),
			CreatedAt = createdAtUtc
		};
		if (id is not null)
			customer.Id = id;
		return customer;
	}
}
