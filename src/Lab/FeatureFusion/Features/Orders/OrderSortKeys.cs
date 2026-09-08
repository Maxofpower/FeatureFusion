using BuildingBlocks.Pagination;
using OrderEntity = FeatureFusion.Domain.Orders.Order;

namespace FeatureFusion.Features.Orders;

/// <summary>Keyset sort keys for Demo Commerce order listing.</summary>
public static class OrderSortKeys
{
	/// <summary>CreatedAt descending, unique Id descending (newest first).</summary>
	public static readonly SortKey<OrderEntity> CreatedAtDesc =
		SortKey.For<OrderEntity>()
			.ByDescending(o => o.CreatedAt, sql: "CreatedAt")
			.ThenByUniqueDescending(o => (int)o.Id, sql: "Id");
}
