using BuildingBlocks.Pagination.EntityFrameworkCore;
using BuildingBlocks.Pagination.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Tests;

public sealed class CatalogContext : DbContext
{
	public CatalogContext(DbContextOptions<CatalogContext> options) : base(options) { }

	public DbSet<CatalogItem> Items => Set<CatalogItem>();
	public DbSet<Vendor> Vendors => Set<Vendor>();
	public DbSet<ShadowItem> Shadows => Set<ShadowItem>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Vendor>().HasKey(v => v.Id);
		modelBuilder.Entity<CatalogItem>().HasKey(i => i.Id);
		modelBuilder.Entity<CatalogItem>().Property(i => i.Id).ValueGeneratedNever();
		modelBuilder.Entity<CatalogItem>().HasOne(i => i.Vendor).WithMany().HasForeignKey(i => i.VendorId);
		modelBuilder.Entity<ShadowItem>().HasKey(s => s.Id);
		modelBuilder.Entity<ShadowItem>().Property(s => s.Id).ValueGeneratedNever();
		modelBuilder.Entity<ShadowItem>().Property<string>("DisplayCode");
	}
}

public sealed class ShadowItem
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
}

public sealed class SqliteFixture : IAsyncLifetime
{
	private SqliteConnection? _connection;

	public CatalogContext CreateContext()
	{
		var options = new DbContextOptionsBuilder<CatalogContext>().UseSqlite(_connection!).Options;
		return new CatalogContext(options);
	}

	public async Task InitializeAsync()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		await _connection.OpenAsync();
		await using var db = CreateContext();
		await db.Database.EnsureCreatedAsync();
		db.Vendors.AddRange(
			new Vendor { Id = 1, Name = "Vendor-X" },
			new Vendor { Id = 2, Name = "Vendor-Y" },
			new Vendor { Id = 3, Name = "Vendor-Z" });
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

		for (var i = 1; i <= 5; i++)
		{
			var shadow = new ShadowItem { Id = i, Name = "S" + i };
			db.Shadows.Add(shadow);
		}

		await db.SaveChangesAsync();
		foreach (var s in db.Shadows)
		{
			db.Entry(s).Property("DisplayCode").CurrentValue = "C" + s.Id;
		}

		await db.SaveChangesAsync();
	}

	public async Task DisposeAsync()
	{
		if (_connection is not null)
		{
			await _connection.DisposeAsync();
		}
	}
}

[CollectionDefinition("sqlite")]
public sealed class SqliteCollection : ICollectionFixture<SqliteFixture>;

[Collection("sqlite")]
public sealed class EfPagingTests(SqliteFixture fixture)
{
	[Theory]
	[MemberData(nameof(EnumMembers))]
	public async Task Enum_First_Page_Each_Field_Asc_And_Desc(ItemSortField field, SortDirection direction)
	{
		await using var db = fixture.CreateContext();
		var key = CatalogSeed.For(field, direction);
		var page = await db.Items.AsQueryable().ToCursorPageAsync(new CursorRequest(null, 5), key);
		Assert.Equal(5, page.Items.Count);
		Assert.True(page.HasNext);
		Assert.False(page.HasPrevious);
		AssertOrdered(page.Items, field, direction);
	}

	public static TheoryData<ItemSortField, SortDirection> EnumMembers()
	{
		var data = new TheoryData<ItemSortField, SortDirection>();
		foreach (var field in Enum.GetValues<ItemSortField>())
		{
			data.Add(field, SortDirection.Ascending);
			data.Add(field, SortDirection.Descending);
		}

		return data;
	}

	[Fact]
	public async Task Forward_No_Overlap_Same_Enum()
	{
		await using var db = fixture.CreateContext();
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 5), CatalogSeed.ById);
		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 5), CatalogSeed.ById);
		Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
		Assert.True(second.Items.Min(i => i.Id) > first.Items.Max(i => i.Id));
		Assert.True(second.HasPrevious);
	}

	[Fact]
	public async Task Previous_Cursor_Restores_First_Page()
	{
		await using var db = fixture.CreateContext();
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 5), CatalogSeed.ById);
		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 5), CatalogSeed.ById);
		var back = await db.Items.ToCursorPageAsync(new CursorRequest(second.Previous, 5), CatalogSeed.ById);
		Assert.Equal(first.Items.Select(i => i.Id), back.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Cursor_Encoded_For_Price_Rejected_On_Name()
	{
		await using var db = fixture.CreateContext();
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 3), CatalogSeed.ByPrice);
		var ex = await Assert.ThrowsAsync<PaginationException>(
			async () => await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 3), CatalogSeed.ByName));
		Assert.Equal(PaginationErrorCode.CursorSortMismatch, ex.Code);
	}

	[Fact]
	public async Task Expression_Query_Does_Not_Use_EF_Property()
	{
		await using var db = fixture.CreateContext();
		var sql = EntityFrameworkCursorExtensions.DebugQueryString(db.Items.AsQueryable(), CatalogSeed.ByPrice, walkBackward: false);
		var expr = EntityFrameworkCursorExtensions.DebugExpressionString(db.Items.AsQueryable(), CatalogSeed.ByPrice, walkBackward: false);
		Assert.DoesNotContain("EF.Property", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("EF.Property", expr, StringComparison.Ordinal);
		Assert.Contains("Price", sql, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task String_Seek_Query_Does_Not_Use_CompareTo()
	{
		await using var db = fixture.CreateContext();
		var first = CatalogSeed.Items[0];
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			CatalogSeed.ByName,
			[first.Name, first.Id],
			walkBackward: false);
		Assert.DoesNotContain("CompareTo", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("string.Compare", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("Compare(\"", sql, StringComparison.Ordinal);
		Assert.Contains('>', sql);
	}

	[Fact]
	public async Task Guid_Seek_Query_Uses_Relational_Operator()
	{
		await using var db = fixture.CreateContext();
		var key = SortKey.For<CatalogItem>().By(x => x.ExternalId).ThenByUnique(x => x.Id);
		var first = CatalogSeed.Items[0];
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			key,
			[first.ExternalId, first.Id],
			walkBackward: false);
		Assert.DoesNotContain("CompareTo", sql, StringComparison.Ordinal);
		Assert.Contains('>', sql);
	}

	[Fact]
	public async Task Mixed_Direction_Seek_Query_Is_Or_Chain()
	{
		await using var db = fixture.CreateContext();
		var key = SortKey.For<CatalogItem>().ByDescending(x => x.Price).ThenByUnique(x => x.Id);
		var first = CatalogSeed.Items.OrderByDescending(i => i.Price).ThenBy(i => i.Id).First();
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			key,
			[first.Price, first.Id],
			walkBackward: false);
		Assert.Contains(" OR ", sql, StringComparison.Ordinal);
		Assert.Contains('<', sql);
		Assert.Contains('>', sql);
		Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Hmac_Signed_Roundtrip_Execute()
	{
		await using var db = fixture.CreateContext();
		var options = new PaginationOptions { SigningKey = "pagination-hmac-test-key-1"u8.ToArray() };
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), CatalogSeed.ById, options);
		Assert.Equal([1, 2, 3, 4], first.Items.Select(i => i.Id));
		Assert.Equal(3, first.Next!.Split('.').Length);
		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), CatalogSeed.ById, options);
		Assert.Equal([5, 6, 7, 8], second.Items.Select(i => i.Id));
		var restored = await db.Items.ToCursorPageAsync(new CursorRequest(second.Previous, 4), CatalogSeed.ById, options);
		Assert.Equal(first.Items.Select(i => i.Id), restored.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Insert_And_Delete_Between_Pages()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		await connection.OpenAsync();
		var options = new DbContextOptionsBuilder<CatalogContext>().UseSqlite(connection).Options;
		await using var db = new CatalogContext(options);
		await db.Database.EnsureCreatedAsync();
		db.Vendors.Add(new Vendor { Id = 1, Name = "Vendor-X" });
		db.Items.AddRange(
			new CatalogItem { Id = 1, Name = "A", Price = 10, CreatedAt = CatalogSeed.T0, Kind = ItemKind.A, ExternalId = Guid.Parse("00000000-0000-0000-0000-000000000001"), LongId = 1, VendorId = 1 },
			new CatalogItem { Id = 2, Name = "B", Price = 10, CreatedAt = CatalogSeed.T0, Kind = ItemKind.A, ExternalId = Guid.Parse("00000000-0000-0000-0000-000000000002"), LongId = 2, VendorId = 1 },
			new CatalogItem { Id = 3, Name = "C", Price = 10, CreatedAt = CatalogSeed.T0, Kind = ItemKind.A, ExternalId = Guid.Parse("00000000-0000-0000-0000-000000000003"), LongId = 3, VendorId = 1 },
			new CatalogItem { Id = 10, Name = "J", Price = 10, CreatedAt = CatalogSeed.T0, Kind = ItemKind.A, ExternalId = Guid.Parse("00000000-0000-0000-0000-000000000010"), LongId = 10, VendorId = 1 },
			new CatalogItem { Id = 11, Name = "K", Price = 10, CreatedAt = CatalogSeed.T0, Kind = ItemKind.A, ExternalId = Guid.Parse("00000000-0000-0000-0000-000000000011"), LongId = 11, VendorId = 1 },
			new CatalogItem { Id = 12, Name = "L", Price = 10, CreatedAt = CatalogSeed.T0, Kind = ItemKind.A, ExternalId = Guid.Parse("00000000-0000-0000-0000-000000000012"), LongId = 12, VendorId = 1 });
		await db.SaveChangesAsync();

		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 3), CatalogSeed.ById);
		Assert.Equal([1, 2, 3], first.Items.Select(i => i.Id));

		db.Items.Add(new CatalogItem
		{
			Id = 5,
			Name = "E",
			Price = 10,
			CreatedAt = CatalogSeed.T0,
			Kind = ItemKind.A,
			ExternalId = Guid.Parse("00000000-0000-0000-0000-000000000005"),
			LongId = 5,
			VendorId = 1
		});
		var gone = await db.Items.FindAsync(10);
		db.Items.Remove(gone!);
		await db.SaveChangesAsync();

		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 3), CatalogSeed.ById);
		Assert.Equal([5, 11, 12], second.Items.Select(i => i.Id));
		Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
	}

	[Fact]
	public async Task ThenByUniqueShadowDescending_Pages()
	{
		await using var db = fixture.CreateContext();
		var key = SortKey.For<ShadowItem>().ThenByUniqueShadowDescending<string>("DisplayCode");
		var expr = EntityFrameworkCursorExtensions.DebugExpressionString(db.Shadows.AsQueryable(), key, walkBackward: false);
		Assert.Contains("Property", expr, StringComparison.Ordinal);
		var page = await db.Shadows.ToCursorPageAsync(new CursorRequest(null, 2), key);
		Assert.Equal(2, page.Items.Count);
		Assert.True(page.HasNext);
	}

	[Fact]
	public async Task Sqlite_Limit_In_Page_Query()
	{
		await using var db = fixture.CreateContext();
		var sql = EntityFrameworkCursorExtensions.DebugQueryString(
			db.Items.AsQueryable(),
			CatalogSeed.ById,
			walkBackward: false,
			take: 5);
		Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void HasKeysetIndex_Matches_SortKey_And_Descending()
	{
		using var connection = new SqliteConnection("Data Source=:memory:");
		connection.Open();
		var options = new DbContextOptionsBuilder<KeysetIndexContext>().UseSqlite(connection).Options;
		using var db = new KeysetIndexContext(options);
		var entity = db.Model.FindEntityType(typeof(CatalogItem))!;
		var priceIndexes = entity.GetIndexes()
			.Where(i => i.Properties.Select(p => p.Name).SequenceEqual(["Price", "Id"]))
			.ToList();
		Assert.Equal(2, priceIndexes.Count);
		Assert.Contains(priceIndexes, i => i.GetDatabaseName() == "IX_catalog_price_id");
		Assert.Contains(priceIndexes, i => i.GetDatabaseName() == "IX_catalog_price_id_desc");
	}

	[Fact]
	public void HasKeysetIndex_Nested_Path_Throws()
	{
		using var db = new NestedIndexContext();
		var ex = Assert.ThrowsAny<Exception>(() => _ = db.Model);
		var pagination = ex as PaginationException
			?? ex.InnerException as PaginationException
			?? ex.GetBaseException() as PaginationException;
		Assert.NotNull(pagination);
		Assert.Equal(PaginationErrorCode.UnsupportedKeysetIndex, pagination.Code);
	}

	[Fact]
	public void No_Public_OrderBy_String_Api()
	{
		var methods = typeof(EntityFrameworkCursorExtensions).GetMethods();
		Assert.DoesNotContain(methods, m => m.Name is "OrderBy" or "ThenBy");
		Assert.DoesNotContain(
			methods,
			m => m.GetParameters().Any(p => p.Name is "propertyName" or "sortBy"));
	}

	[Fact]
	public async Task Shadow_Query_Uses_EF_Property()
	{
		await using var db = fixture.CreateContext();
		var key = SortKey.For<ShadowItem>().ByShadow<string>("DisplayCode").ThenByUnique(s => s.Id);
		var expr = EntityFrameworkCursorExtensions.DebugExpressionString(db.Shadows.AsQueryable(), key, walkBackward: false);
		Assert.Contains("Property", expr, StringComparison.Ordinal);
		var sql = EntityFrameworkCursorExtensions.DebugQueryString(db.Shadows.AsQueryable(), key, walkBackward: false);
		Assert.Contains("DisplayCode", sql, StringComparison.Ordinal);
		var page = await db.Shadows.ToCursorPageAsync(new CursorRequest(null, 2), key);
		Assert.Equal(2, page.Items.Count);
		Assert.True(page.HasNext);
	}

	[Fact]
	public async Task Unknown_Shadow_Fails_Before_Execute()
	{
		await using var db = fixture.CreateContext();
		var key = SortKey.For<ShadowItem>().ByShadow<string>("Nope").ThenByUnique(s => s.Id);
		var ex = await Assert.ThrowsAsync<PaginationException>(
			async () => await db.Shadows.ToCursorPageAsync(new CursorRequest(null, 2), key));
		Assert.Equal(PaginationErrorCode.UnknownShadowProperty, ex.Code);
	}

	[Fact]
	public async Task Nested_Vendor_Name()
	{
		await using var db = fixture.CreateContext();
		var key = SortKey.For<CatalogItem>().By(x => x.Vendor!.Name).ThenByUnique(x => x.Id);
		var page = await db.Items.Include(i => i.Vendor).ToCursorPageAsync(new CursorRequest(null, 4), key);
		Assert.Equal(4, page.Items.Count);
		var names = page.Items.Select(i => i.Vendor!.Name).ToList();
		Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ThenBy(n => 0).Take(4).ToList(), names);
	}

	[Fact]
	public async Task Empty_Table()
	{
		await using var db = fixture.CreateContext();
		var page = await db.Items.Where(i => i.Id < 0).ToCursorPageAsync(new CursorRequest(null, 5), CatalogSeed.ById);
		Assert.Empty(page.Items);
		Assert.False(page.HasNext);
	}

	[Fact]
	public async Task Single_Page_HasNext_False()
	{
		await using var db = fixture.CreateContext();
		var page = await db.Items.ToCursorPageAsync(new CursorRequest(null, 50), CatalogSeed.ById);
		Assert.Equal(12, page.Items.Count);
		Assert.False(page.HasNext);
	}

	[Fact]
	public async Task Duplicate_Prices_Stable()
	{
		await using var db = fixture.CreateContext();
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), CatalogSeed.ByPrice);
		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), CatalogSeed.ByPrice);
		var ids = first.Items.Concat(second.Items).Select(i => i.Id).ToList();
		Assert.Equal(ids.Distinct().Count(), ids.Count);
	}

	[Fact]
	public async Task IncludeTotalCount_On_Off()
	{
		await using var db = fixture.CreateContext();
		var off = await db.Items.ToCursorPageAsync(new CursorRequest(null, 3), CatalogSeed.ById);
		Assert.Null(off.TotalCount);
		var on = await db.Items.ToCursorPageAsync(
			new CursorRequest(null, 3),
			CatalogSeed.ById,
			new PaginationOptions { IncludeTotalCount = true });
		Assert.Equal(12, on.TotalCount);
	}

	[Fact]
	public async Task Invalid_Cursor()
	{
		await using var db = fixture.CreateContext();
		await Assert.ThrowsAsync<PaginationException>(
			async () => await db.Items.ToCursorPageAsync(new CursorRequest("not-a-valid-cursor", 5), CatalogSeed.ById));
	}

	[Fact]
	public async Task Cancellation()
	{
		await using var db = fixture.CreateContext();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			async () => await db.Items.ToCursorPageAsync(new CursorRequest(null, 5), CatalogSeed.ById, cancellationToken: cts.Token));
	}

	[Fact]
	public async Task Projection_Includes_Keys()
	{
		await using var db = fixture.CreateContext();
		var page = await db.Items.ToCursorPageAsync(
			new CursorRequest(null, 3),
			CatalogSeed.ByPrice,
			i => new CatalogItemDto { Id = i.Id, Name = i.Name, Price = i.Price });
		Assert.Equal(3, page.Items.Count);
		var next = await db.Items.ToCursorPageAsync(
			new CursorRequest(page.Next, 3),
			CatalogSeed.ByPrice,
			i => new CatalogItemDto { Id = i.Id, Name = i.Name, Price = i.Price });
		Assert.Empty(page.Items.Select(i => i.Id).Intersect(next.Items.Select(i => i.Id)));
	}

	[Fact]
	public async Task Projection_Omitting_Key_Fails()
	{
		await using var db = fixture.CreateContext();
		await Assert.ThrowsAsync<PaginationException>(
			async () => await db.Items.ToCursorPageAsync(
				new CursorRequest(null, 3),
				CatalogSeed.ById,
				i => new CatalogItemDtoMissingId { Name = i.Name, Price = i.Price }));
	}

	[Fact]
	public async Task InMemory_Map()
	{
		await using var db = fixture.CreateContext();
		var page = await db.Items.ToCursorPageMappedAsync(
			new CursorRequest(null, 2),
			CatalogSeed.ById,
			i => i.Name);
		Assert.Equal(2, page.Items.Count);
		Assert.All(page.Items, n => Assert.StartsWith("Item-", n, StringComparison.Ordinal));
	}

	[Fact]
	public async Task Clr_Guid_Long_Enum()
	{
		await using var db = fixture.CreateContext();
		var byGuid = SortKey.For<CatalogItem>().By(x => x.ExternalId).ThenByUnique(x => x.Id);
		var byLong = SortKey.For<CatalogItem>().By(x => x.LongId).ThenByUnique(x => x.Id);
		var byKind = SortKey.For<CatalogItem>().By(x => x.Kind).ThenByUnique(x => x.Id);
		Assert.Equal(4, (await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), byGuid)).Items.Count);
		Assert.Equal(4, (await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), byLong)).Items.Count);
		Assert.Equal(4, (await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), byKind)).Items.Count);
		Assert.Equal(
			PaginationErrorCode.NullableSortUnsupported,
			Assert.Throws<PaginationException>(
				() => SortKey.For<CatalogItem>().By(x => x.OptionalAt).ThenByUnique(x => x.Id)).Code);
	}

	[Fact]
	public async Task Clr_Decimal_InMemory()
	{
		var options = new DbContextOptionsBuilder<DecimalContext>()
			.UseInMemoryDatabase("decimal-" + Guid.NewGuid().ToString("N"))
			.Options;
		await using var db = new DecimalContext(options);
		db.Rows.AddRange(
			new DecimalItem { Id = 1, Amount = 1.5m },
			new DecimalItem { Id = 2, Amount = 1.5m },
			new DecimalItem { Id = 3, Amount = 2.0m });
		await db.SaveChangesAsync();
		var key = SortKey.For<DecimalItem>().By(x => x.Amount).ThenByUnique(x => x.Id);
		var first = await db.Rows.ToCursorPageAsync(new CursorRequest(null, 2), key);
		Assert.Equal([1, 2], first.Items.Select(r => r.Id));
		var second = await db.Rows.ToCursorPageAsync(new CursorRequest(first.Next, 2), key);
		Assert.Equal([3], second.Items.Select(r => r.Id));
	}

	[Fact]
	public async Task Clr_DateTimeOffset_InMemory()
	{
		var options = new DbContextOptionsBuilder<OffsetContext>()
			.UseInMemoryDatabase("offset-" + Guid.NewGuid().ToString("N"))
			.Options;
		await using var db = new OffsetContext(options);
		var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
		db.Rows.AddRange(
			new OffsetItem { Id = 1, At = t0.AddHours(2) },
			new OffsetItem { Id = 2, At = t0.AddHours(1) },
			new OffsetItem { Id = 3, At = t0.AddHours(3) });
		await db.SaveChangesAsync();
		var key = SortKey.For<OffsetItem>().By(x => x.At).ThenByUnique(x => x.Id);
		var page = await db.Rows.ToCursorPageAsync(new CursorRequest(null, 2), key);
		Assert.Equal([2, 1], page.Items.Select(r => r.Id));
	}

	[Fact]
	public async Task Name_Next_Page_No_Overlap()
	{
		await using var db = fixture.CreateContext();
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), CatalogSeed.ByName);
		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), CatalogSeed.ByName);
		Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
	}

	[Fact]
	public async Task Limit_Over_Max_Throws()
	{
		await using var db = fixture.CreateContext();
		var ex = await Assert.ThrowsAsync<PaginationException>(
			async () => await db.Items.ToCursorPageAsync(new CursorRequest(null, 101), CatalogSeed.ById));
		Assert.Equal(PaginationErrorCode.InvalidLimit, ex.Code);
	}

	[Fact]
	public async Task Hmac_Rejects_Unsigned()
	{
		await using var db = fixture.CreateContext();
		var unsigned = await db.Items.ToCursorPageAsync(new CursorRequest(null, 3), CatalogSeed.ById);
		var signed = new PaginationOptions { SigningKey = "super-secret-key-1"u8.ToArray() };
		var ex = await Assert.ThrowsAsync<PaginationException>(
			async () => await db.Items.ToCursorPageAsync(new CursorRequest(unsigned.Next, 3), CatalogSeed.ById, signed));
		Assert.Equal(PaginationErrorCode.InvalidCursor, ex.Code);
	}

	[Fact]
	public async Task Host_Where_Filter()
	{
		await using var db = fixture.CreateContext();
		var page = await db.Items.Where(i => i.Price >= 20d)
			.ToCursorPageAsync(new CursorRequest(null, 10), CatalogSeed.ById, new PaginationOptions { IncludeTotalCount = true });
		Assert.All(page.Items, i => Assert.True(i.Price >= 20d));
		Assert.Equal(page.Items.Count, page.TotalCount);
	}

	[Fact]
	public async Task QueryHint_ReadUncommitted_On_Sqlite_Is_Noop()
	{
		await using var db = fixture.CreateContext();
		var page = await db.Items.ToCursorPageAsync(
			new CursorRequest(null, 5),
			CatalogSeed.ById,
			new PaginationOptions { Hint = QueryHint.ReadUncommitted });
		Assert.Equal(5, page.Items.Count);
		var sql = EntityFrameworkCursorExtensions.DebugQueryString(
			db.Items.AsQueryable(), CatalogSeed.ById, walkBackward: false, take: 6);
		Assert.DoesNotContain("READ UNCOMMITTED", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("NOLOCK", sql, StringComparison.Ordinal);
	}

	private static void AssertOrdered(IReadOnlyList<CatalogItem> items, ItemSortField field, SortDirection direction)
	{
		IEnumerable<CatalogItem> expected = field switch
		{
			ItemSortField.Id => CatalogSeed.Items,
			ItemSortField.Name => CatalogSeed.Items.OrderBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Id),
			ItemSortField.Price => CatalogSeed.Items.OrderBy(i => i.Price).ThenBy(i => i.Id),
			ItemSortField.CreatedAt => CatalogSeed.Items.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id),
			_ => throw new ArgumentOutOfRangeException(nameof(field))
		};
		if (direction == SortDirection.Descending)
		{
			expected = field switch
			{
				ItemSortField.Id => CatalogSeed.Items.OrderByDescending(i => i.Id),
				ItemSortField.Name => CatalogSeed.Items.OrderByDescending(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Id),
				ItemSortField.Price => CatalogSeed.Items.OrderByDescending(i => i.Price).ThenBy(i => i.Id),
				ItemSortField.CreatedAt => CatalogSeed.Items.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id),
				_ => expected
			};
		}

		Assert.Equal(expected.Take(5).Select(i => i.Id), items.Select(i => i.Id));
	}
}

public sealed class DecimalContext : DbContext
{
	public DecimalContext(DbContextOptions<DecimalContext> options) : base(options) { }

	public DbSet<DecimalItem> Rows => Set<DecimalItem>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<DecimalItem>().HasKey(r => r.Id);
		modelBuilder.Entity<DecimalItem>().Property(r => r.Id).ValueGeneratedNever();
	}
}

file sealed class KeysetIndexContext : DbContext
{
	public KeysetIndexContext(DbContextOptions<KeysetIndexContext> options) : base(options) { }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<CatalogItem>().HasKey(i => i.Id);
		modelBuilder.Entity<CatalogItem>().HasKeysetIndex(CatalogSeed.ByPrice).HasDatabaseName("IX_catalog_price_id");
		modelBuilder.Entity<CatalogItem>().HasKeysetIndex(CatalogSeed.ByPriceDesc).HasDatabaseName("IX_catalog_price_id_desc");
	}
}

file sealed class NestedIndexContext : DbContext
{
	public NestedIndexContext()
		: base(new DbContextOptionsBuilder<NestedIndexContext>()
			.UseInMemoryDatabase("nested-index-" + Guid.NewGuid().ToString("N"))
			.Options)
	{
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Vendor>().HasKey(v => v.Id);
		modelBuilder.Entity<CatalogItem>().HasKey(i => i.Id);
		modelBuilder.Entity<CatalogItem>().HasOne(i => i.Vendor).WithMany().HasForeignKey(i => i.VendorId);
		modelBuilder.Entity<CatalogItem>().HasKeysetIndex(
			SortKey.For<CatalogItem>().By(x => x.Vendor!.Name).ThenByUnique(x => x.Id));
	}
}

public sealed class OffsetContext : DbContext
{
	public OffsetContext(DbContextOptions<OffsetContext> options) : base(options) { }

	public DbSet<OffsetItem> Rows => Set<OffsetItem>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<OffsetItem>().HasKey(r => r.Id);
		modelBuilder.Entity<OffsetItem>().Property(r => r.Id).ValueGeneratedNever();
	}
}
