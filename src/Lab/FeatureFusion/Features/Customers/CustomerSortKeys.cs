using BuildingBlocks.Pagination;
using FeatureFusion.Domain.Customers;

namespace FeatureFusion.Features.Customers;

/// <summary>Keyset sort keys for Demo Commerce customer listing.</summary>
public static class CustomerSortKeys
{
	/// <summary>CreatedAt descending, unique Id descending (newest first).</summary>
	public static readonly SortKey<Customer> CreatedAtDesc =
		SortKey.For<Customer>()
			.ByDescending(c => c.CreatedAt, sql: "CreatedAt")
			.ThenByUniqueDescending(c => (int)c.Id, sql: "Id");
}
