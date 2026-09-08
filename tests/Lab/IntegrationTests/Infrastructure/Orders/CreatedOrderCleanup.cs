using FeatureFusion.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests.Infrastructure.Orders;

/// <summary>
/// Removes runtime CreateOrder rows (ORD-{Ulid}) so Demo Commerce seed fixtures stay observable.
/// Does not touch seed order numbers (ORD-PWR-*, ORD-HIGH-001, ORD-####).
/// </summary>
internal static class CreatedOrderCleanup
{
	public static async Task DeleteByDomainIdsAsync(IServiceProvider services, params int[] domainOrderIds)
	{
		if (domainOrderIds.Length == 0)
			return;

		await using var scope = services.CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		var orders = await db.Orders
			.Include(o => o.Items)
			.Where(o => domainOrderIds.Contains((int)o.Id))
			.ToListAsync()
			.ConfigureAwait(false);

		if (orders.Count == 0)
			return;

		db.OrderItems.RemoveRange(orders.SelectMany(o => o.Items));
		db.Orders.RemoveRange(orders);
		await db.SaveChangesAsync().ConfigureAwait(false);
	}
}
