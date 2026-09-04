using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Pagination.EntityFrameworkCore;

/// <summary>
/// DI and <see cref="DbContextOptionsBuilder"/> registration for pagination infrastructure
/// (ORDER BY <c>NULLS FIRST/LAST</c> command interceptor).
/// </summary>
/// <remarks>
/// LINQ has no <c>NULLS FIRST/LAST</c>. The interceptor is the index-honest path on Npgsql
/// and Sqlite. Pair <see cref="AddBuildingBlocksPagination"/> with
/// <see cref="UseBuildingBlocksPagination(DbContextOptionsBuilder)"/> on every context.
/// </remarks>
public static class PaginationServiceCollectionExtensions
{
	/// <summary>
	/// Registers the shared <c>NULLS FIRST/LAST</c> interceptor singleton. Hosts must also call
	/// <see cref="UseBuildingBlocksPagination(DbContextOptionsBuilder)"/> on the context options
	/// so EF attaches it. Without that, <see cref="NullOrder"/> remains seek-predicate only for EF.
	/// </summary>
	/// <param name="services">Service collection.</param>
	/// <returns>The same collection.</returns>
	public static IServiceCollection AddBuildingBlocksPagination(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton(PaginationNullsInterceptor.Instance);
		return services;
	}

	/// <summary>
	/// Adds the pagination <c>NULLS FIRST/LAST</c> interceptor to this context options builder.
	/// Pair with <see cref="AddBuildingBlocksPagination"/>.
	/// </summary>
	/// <param name="optionsBuilder">EF options.</param>
	/// <returns>The same builder.</returns>
	public static DbContextOptionsBuilder UseBuildingBlocksPagination(this DbContextOptionsBuilder optionsBuilder)
	{
		ArgumentNullException.ThrowIfNull(optionsBuilder);
		optionsBuilder.AddInterceptors(PaginationNullsInterceptor.Instance);
		return optionsBuilder;
	}

	/// <inheritdoc cref="UseBuildingBlocksPagination(DbContextOptionsBuilder)"/>
	public static DbContextOptionsBuilder<TContext> UseBuildingBlocksPagination<TContext>(
		this DbContextOptionsBuilder<TContext> optionsBuilder)
		where TContext : DbContext
	{
		((DbContextOptionsBuilder)optionsBuilder).UseBuildingBlocksPagination();
		return optionsBuilder;
	}
}
