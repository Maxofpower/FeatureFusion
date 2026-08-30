using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using BuildingBlocks.Pagination.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Tests;

/// <summary>
/// Isolated Sqlite execute tests: sort-column mutation (keyset may skip/duplicate),
/// nullable string NullOrder, and cancellation after the command has started.
/// </summary>
public sealed class MutationNullCancelEfTests
{
	[Fact]
	public async Task SortColumn_Price_Raise_Vanishes_From_Next_Page()
	{
		await using var db = await SeedPriceAsync();
		var key = SortKey.For<CatalogItem>().By(x => x.Price).ThenByUnique(x => x.Id);
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 2), key);
		Assert.Equal([1, 2], first.Items.Select(i => i.Id));

		var later = await db.Items.FindAsync(3);
		later!.Price = 5;
		await db.SaveChangesAsync();

		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 2), key);
		Assert.DoesNotContain(3, second.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task SortColumn_Price_Lower_Can_Appear_On_Next_Page()
	{
		await using var db = await SeedPriceAsync();
		var key = SortKey.For<CatalogItem>().By(x => x.Price).ThenByUnique(x => x.Id);
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 2), key);
		Assert.Equal([1, 2], first.Items.Select(i => i.Id));

		var far = await db.Items.FindAsync(6);
		far!.Price = 25;
		await db.SaveChangesAsync();

		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), key);
		Assert.Contains(6, second.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task SortColumn_DateTime_Raise_Vanishes_From_Next_Page()
	{
		await using var db = await SeedPriceAsync();
		var key = SortKey.For<CatalogItem>().By(x => x.CreatedAt).ThenByUnique(x => x.Id);
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 2), key);
		Assert.Equal([1, 2], first.Items.Select(i => i.Id));

		var later = await db.Items.FindAsync(3);
		later!.CreatedAt = CatalogSeed.T0.AddDays(-1);
		await db.SaveChangesAsync();

		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 2), key);
		Assert.DoesNotContain(3, second.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Nullable_String_Forward_And_Backward_Last_And_First()
	{
		await using var db = await SeedLabelsAsync();
		var key = SortKey.For<LabelRow>().By(x => x.Label).ThenByUnique(x => x.Id);

		// Sqlite ASC places NULL first. NullOrder is seek-predicate only (no NULLS FIRST/LAST).
		var lastFwd = await db.Labels.ToCursorPageAsync(
			new CursorRequest(null, 10), key, new PaginationOptions { Nulls = NullOrder.Last });
		var firstFwd = await db.Labels.ToCursorPageAsync(
			new CursorRequest(null, 10), key, new PaginationOptions { Nulls = NullOrder.First });
		Assert.Equal(new[] { 2, 3, 1, 4 }, lastFwd.Items.Select(r => r.Id));
		Assert.Equal(lastFwd.Items.Select(r => r.Id), firstFwd.Items.Select(r => r.Id));
		Assert.Null(lastFwd.Items[0].Label);
		Assert.Equal("", lastFwd.Items[1].Label);

		var lastBack = await db.Labels.ToCursorPageAsync(
			new CursorRequest(null, 10, PageDirection.Backward), key, new PaginationOptions { Nulls = NullOrder.Last });
		Assert.Equal(lastFwd.Items.Select(r => r.Id), lastBack.Items.Select(r => r.Id));

		var afterNull = await db.Labels.ToCursorPageAsync(
			new CursorRequest(null, 1), key, new PaginationOptions { Nulls = NullOrder.Last });
		Assert.Equal(2, afterNull.Items[0].Id);

		var lastSeek = await db.Labels.ToCursorPageAsync(
			new CursorRequest(afterNull.Next, 10), key, new PaginationOptions { Nulls = NullOrder.Last });
		Assert.Equal(new[] { 3, 1, 4 }, lastSeek.Items.Select(r => r.Id));

		var firstSeek = await db.Labels.ToCursorPageAsync(
			new CursorRequest(afterNull.Next, 10), key, new PaginationOptions { Nulls = NullOrder.First });
		Assert.Empty(firstSeek.Items);

		var paged = await db.Labels.ToCursorPageAsync(
			new CursorRequest(null, 2), key, new PaginationOptions { Nulls = NullOrder.Last });
		var next = await db.Labels.ToCursorPageAsync(
			new CursorRequest(paged.Next, 2), key, new PaginationOptions { Nulls = NullOrder.Last });
		Assert.Empty(paged.Items.Select(r => r.Id).Intersect(next.Items.Select(r => r.Id)));
	}

	[Fact]
	public async Task Cancellation_After_Command_Starts()
	{
		using var cts = new CancellationTokenSource();
		using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var options = new DbContextOptionsBuilder<CatalogContext>()
			.UseSqlite(connection)
			.AddInterceptors(new CancelOnReaderInterceptor(cts))
			.Options;
		await using var db = new CatalogContext(options);
		await db.Database.EnsureCreatedAsync();
		db.Vendors.Add(new Vendor { Id = 1, Name = "Vendor-X" });
		db.Items.Add(new CatalogItem
		{
			Id = 1,
			Name = "A",
			Price = 10,
			CreatedAt = CatalogSeed.T0,
			Kind = ItemKind.A,
			ExternalId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
			LongId = 1,
			VendorId = 1
		});
		await db.SaveChangesAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			async () => await db.Items.ToCursorPageAsync(
				new CursorRequest(null, 5),
				CatalogSeed.ById,
				cancellationToken: cts.Token));
	}

	private static async Task<CatalogContext> SeedPriceAsync()
	{
		var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var options = new DbContextOptionsBuilder<CatalogContext>().UseSqlite(connection).Options;
		var db = new CatalogContext(options);
		await db.Database.EnsureCreatedAsync();
		db.Vendors.Add(new Vendor { Id = 1, Name = "Vendor-X" });
		for (var i = 1; i <= 6; i++)
		{
			db.Items.Add(new CatalogItem
			{
				Id = i,
				Name = "N" + i,
				Price = i * 10d,
				CreatedAt = CatalogSeed.T0.AddDays(i),
				Kind = ItemKind.A,
				ExternalId = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
				LongId = i,
				VendorId = 1
			});
		}

		await db.SaveChangesAsync();
		return db;
	}

	private static async Task<LabelContext> SeedLabelsAsync()
	{
		var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var options = new DbContextOptionsBuilder<LabelContext>().UseSqlite(connection).Options;
		var db = new LabelContext(options);
		await db.Database.EnsureCreatedAsync();
		db.Labels.AddRange(
			new LabelRow { Id = 1, Label = "alpha" },
			new LabelRow { Id = 2, Label = null },
			new LabelRow { Id = 3, Label = "" },
			new LabelRow { Id = 4, Label = "beta" });
		await db.SaveChangesAsync();
		return db;
	}
}

public sealed class LabelRow
{
	public int Id { get; set; }
	public string? Label { get; set; }
}

public sealed class LabelContext : DbContext
{
	public LabelContext(DbContextOptions<LabelContext> options) : base(options) { }

	public DbSet<LabelRow> Labels => Set<LabelRow>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<LabelRow>().HasKey(r => r.Id);
		modelBuilder.Entity<LabelRow>().Property(r => r.Id).ValueGeneratedNever();
	}
}

file sealed class CancelOnReaderInterceptor : DbCommandInterceptor
{
	private readonly CancellationTokenSource _cts;

	public CancelOnReaderInterceptor(CancellationTokenSource cts) => _cts = cts;

	public override InterceptionResult<DbDataReader> ReaderExecuting(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result)
	{
		_cts.Cancel();
		return base.ReaderExecuting(command, eventData, result);
	}

	public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result,
		CancellationToken cancellationToken = default)
	{
		_cts.Cancel();
		return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
	}
}
