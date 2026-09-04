using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using BuildingBlocks.Pagination.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Tests;

/// <summary>
/// Model-only (no container). Asserts migration SQL, which is what <c>HasNullSortOrder</c> is for.
/// Separate context types so EF does not reuse a compiled model across the with/without cases.
/// </summary>
public sealed class HasKeysetIndexNullSortEfTests
{
	[Fact]
	public void HasKeysetIndex_NullOrder_Emits_Npgsql_Nulls_In_Create_Script()
	{
		using var withNulls = new WithNullSortContext();
		using var withoutNulls = new WithoutNullSortContext();

		var withScript = withNulls.Database.GenerateCreateScript();
		var withoutScript = withoutNulls.Database.GenerateCreateScript();

		Assert.Contains(NullSortIndexModel.IndexName, withScript, StringComparison.Ordinal);
		Assert.Contains("NULLS FIRST", withScript, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("NULLS FIRST", withoutScript, StringComparison.OrdinalIgnoreCase);
	}
}

file static class NullSortIndexModel
{
	public const string IndexName = "IX_catalog_price_id";

	public static DbContextOptions<TContext> Options<TContext>()
		where TContext : DbContext
		=> new DbContextOptionsBuilder<TContext>()
			.UseNpgsql("Host=127.0.0.1;Database=pagination_nullsort_smoke")
			.Options;

	public static void Catalog(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<CatalogItem>().HasKey(i => i.Id);
		modelBuilder.Entity<CatalogItem>().Property(i => i.Id).ValueGeneratedNever();
	}
}

file sealed class WithNullSortContext : DbContext
{
	public WithNullSortContext() : base(NullSortIndexModel.Options<WithNullSortContext>()) { }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		NullSortIndexModel.Catalog(modelBuilder);
		modelBuilder.Entity<CatalogItem>()
			.HasKeysetIndex(CatalogSeed.ByPrice, NullOrder.First)
			.HasDatabaseName(NullSortIndexModel.IndexName);
	}
}

file sealed class WithoutNullSortContext : DbContext
{
	public WithoutNullSortContext() : base(NullSortIndexModel.Options<WithoutNullSortContext>()) { }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		NullSortIndexModel.Catalog(modelBuilder);
		modelBuilder.Entity<CatalogItem>()
			.HasKeysetIndex(CatalogSeed.ByPrice)
			.HasDatabaseName(NullSortIndexModel.IndexName);
	}
}
