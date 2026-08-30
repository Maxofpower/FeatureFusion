using BuildingBlocks.Pagination.EntityFrameworkCore;
using BuildingBlocks.Pagination.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Tests;

public sealed class TypedContext : DbContext
{
	public TypedContext(DbContextOptions<TypedContext> options) : base(options) { }

	public DbSet<TypedRow> Rows => Set<TypedRow>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<TypedRow>().HasKey(r => r.Id);
		modelBuilder.Entity<TypedRow>().Property(r => r.Id).ValueGeneratedNever();
		modelBuilder.Entity<TypedRow>().OwnsOne(r => r.Price);
		modelBuilder.Entity<TypedRow>().Ignore(r => r.TypedId);
		modelBuilder.Entity<TypedRow>().Ignore(r => r.Payload);
	}
}

public sealed class TypesAndSeekEfTests
{
	private static async Task<TypedContext> SeedAsync()
	{
		var options = new DbContextOptionsBuilder<TypedContext>()
			.UseInMemoryDatabase("typed-" + Guid.NewGuid().ToString("N"))
			.Options;
		var db = new TypedContext(options);
		db.Rows.AddRange(
			new TypedRow
			{
				Id = 1,
				Flag = false,
				OptionalFlag = null,
				Day = new DateOnly(2024, 1, 3),
				Clock = new TimeOnly(8, 0),
				Duration = TimeSpan.FromMinutes(3),
				ShortId = 3,
				Tiny = 3,
				Ratio = 0.3f,
				OptionalKind = null,
				Price = new Money { Amount = 30m }
			},
			new TypedRow
			{
				Id = 2,
				Flag = true,
				OptionalFlag = true,
				Day = new DateOnly(2024, 1, 1),
				Clock = new TimeOnly(7, 0),
				Duration = TimeSpan.FromMinutes(1),
				ShortId = 1,
				Tiny = 1,
				Ratio = 0.1f,
				OptionalKind = ItemKind.A,
				Price = new Money { Amount = 10m }
			},
			new TypedRow
			{
				Id = 3,
				Flag = true,
				OptionalFlag = false,
				Day = new DateOnly(2024, 1, 2),
				Clock = new TimeOnly(9, 0),
				Duration = TimeSpan.FromMinutes(2),
				ShortId = 2,
				Tiny = 2,
				Ratio = 0.2f,
				OptionalKind = ItemKind.B,
				Price = new Money { Amount = 20m }
			});
		await db.SaveChangesAsync();
		return db;
	}

	[Fact]
	public async Task Clr_Bool_DateOnly_TimeOnly_TimeSpan_And_Nested_Money()
	{
		await using var db = await SeedAsync();
		var byFlag = SortKey.For<TypedRow>().By(x => x.Flag).ThenByUnique(x => x.Id);
		var flags = await db.Rows.ToCursorPageAsync(new CursorRequest(null, 2), byFlag);
		Assert.Equal([1, 2], flags.Items.Select(r => r.Id));
		var nextFlags = await db.Rows.ToCursorPageAsync(new CursorRequest(flags.Next, 2), byFlag);
		Assert.Equal([3], nextFlags.Items.Select(r => r.Id));

		var byDay = SortKey.For<TypedRow>().By(x => x.Day).ThenByUnique(x => x.Id);
		Assert.Equal([2, 3, 1], (await db.Rows.ToCursorPageAsync(new CursorRequest(null, 10), byDay)).Items.Select(r => r.Id));

		var byClock = SortKey.For<TypedRow>().By(x => x.Clock).ThenByUnique(x => x.Id);
		Assert.Equal([2, 1, 3], (await db.Rows.ToCursorPageAsync(new CursorRequest(null, 10), byClock)).Items.Select(r => r.Id));

		var byDuration = SortKey.For<TypedRow>().By(x => x.Duration).ThenByUnique(x => x.Id);
		Assert.Equal([2, 3, 1], (await db.Rows.ToCursorPageAsync(new CursorRequest(null, 10), byDuration)).Items.Select(r => r.Id));

		var byMoney = SortKey.For<TypedRow>().By(x => x.Price.Amount).ThenByUnique(x => x.Id);
		var money = await db.Rows.ToCursorPageAsync(new CursorRequest(null, 2), byMoney);
		Assert.Equal([2, 3], money.Items.Select(r => r.Id));
		var moneyNext = await db.Rows.ToCursorPageAsync(new CursorRequest(money.Next, 2), byMoney);
		Assert.Equal([1], moneyNext.Items.Select(r => r.Id));
	}

	[Fact]
	public void Nullable_Sort_Slots_Are_Rejected()
	{
		Assert.Equal(
			PaginationErrorCode.NullableSortUnsupported,
			Assert.Throws<PaginationException>(
				() => SortKey.For<TypedRow>().By(x => x.OptionalKind).ThenByUnique(x => x.Id)).Code);
		Assert.Equal(
			PaginationErrorCode.NullableSortUnsupported,
			Assert.Throws<PaginationException>(
				() => SortKey.For<TypedRow>().By(x => x.OptionalFlag).ThenByUnique(x => x.Id)).Code);
		Assert.Equal(
			PaginationErrorCode.NullableSortUnsupported,
			Assert.Throws<PaginationException>(
				() => SortKey.For<CatalogItem>().By(x => x.OptionalAt).ThenByUnique(x => x.Id)).Code);
	}

	[Fact]
	public async Task Backward_Empty_Cursor_Is_Last_Page()
	{
		await using var db = fixtureContext();
		var page = await db.Items.ToCursorPageAsync(
			new CursorRequest(null, 5, PageDirection.Backward),
			CatalogSeed.ById);
		Assert.Equal(CatalogSeed.Items.Select(i => i.Id).Skip(7).Take(5), page.Items.Select(i => i.Id));
		Assert.True(page.HasPrevious);
		Assert.False(page.HasNext);
	}

	[Fact]
	public async Task Replay_Same_Cursor_Is_Stable()
	{
		await using var db = fixtureContext();
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), CatalogSeed.ById);
		var a = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), CatalogSeed.ById);
		var b = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), CatalogSeed.ById);
		Assert.Equal(a.Items.Select(i => i.Id), b.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Whitespace_Cursor_Is_First_Page()
	{
		await using var db = fixtureContext();
		var empty = await db.Items.ToCursorPageAsync(new CursorRequest(null, 3), CatalogSeed.ById);
		var whitespace = await db.Items.ToCursorPageAsync(new CursorRequest("  ", 3), CatalogSeed.ById);
		Assert.Equal(empty.Items.Select(i => i.Id), whitespace.Items.Select(i => i.Id));
	}

	[Fact]
	public void Nullable_DateTime_Sort_Slot_Is_Rejected()
	{
		var ex = Assert.Throws<PaginationException>(
			() => SortKey.For<CatalogItem>().By(x => x.OptionalAt).ThenByUnique(x => x.Id));
		Assert.Equal(PaginationErrorCode.NullableSortUnsupported, ex.Code);
	}

	private static CatalogContext fixtureContext()
	{
		var options = new DbContextOptionsBuilder<CatalogContext>()
			.UseInMemoryDatabase("catalog-types-" + Guid.NewGuid().ToString("N"))
			.Options;
		var db = new CatalogContext(options);
		db.Vendors.AddRange(CatalogSeed.Items.Select(i => i.Vendor).DistinctBy(v => v!.Id)!);
		foreach (var item in CatalogSeed.Items)
		{
			db.Items.Add(new CatalogItem
			{
				Id = item.Id,
				Name = item.Name,
				Price = item.Price,
				CreatedAt = item.CreatedAt,
				Kind = item.Kind,
				ExternalId = item.ExternalId,
				LongId = item.LongId,
				OptionalAt = item.OptionalAt,
				VendorId = item.VendorId
			});
		}

		db.SaveChanges();
		return db;
	}
}
