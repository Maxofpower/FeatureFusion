using System.Data;
using System.Reflection;
using BuildingBlocks.Pagination.Dapper;
using BuildingBlocks.Pagination.TestSupport;
using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BuildingBlocks.Pagination.Dapper.Tests;

public sealed class SqliteDapperFixture : IAsyncLifetime
{
	private SqliteConnection? _keepAlive;

	public IDbConnection Open()
	{
		var connection = new SqliteConnection("Data Source=file:pagetests?mode=memory&cache=shared");
		connection.Open();
		return connection;
	}

	public async Task InitializeAsync()
	{
		_keepAlive = new SqliteConnection("Data Source=file:pagetests?mode=memory&cache=shared");
		_keepAlive.Open();
		await _keepAlive.ExecuteAsync("""
			CREATE TABLE CatalogItem (
			  Id INTEGER PRIMARY KEY,
			  Name TEXT NOT NULL,
			  Price REAL NOT NULL,
			  CreatedAt TEXT NOT NULL,
			  Kind INTEGER NOT NULL,
			  ExternalId TEXT NOT NULL,
			  LongId INTEGER NOT NULL,
			  OptionalAt TEXT NULL
			);
			CREATE TABLE DecimalItem (
			  Id INTEGER PRIMARY KEY,
			  Amount NUMERIC NOT NULL
			);
			CREATE TABLE OffsetItem (
			  Id INTEGER PRIMARY KEY,
			  At TEXT NOT NULL
			);
			""");
		SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
		SqlMapper.AddTypeHandler(new GuidHandler());
		SqlMapper.AddTypeHandler(new DecimalHandler());
		foreach (var item in CatalogSeed.Items)
		{
			await _keepAlive.ExecuteAsync(
				"""
				INSERT INTO CatalogItem (Id, Name, Price, CreatedAt, Kind, ExternalId, LongId, OptionalAt)
				VALUES (@Id, @Name, @Price, @CreatedAt, @Kind, @ExternalId, @LongId, @OptionalAt);
				""",
				new
				{
					item.Id,
					item.Name,
					item.Price,
					CreatedAt = item.CreatedAt.ToString("O"),
					Kind = (int)item.Kind,
					ExternalId = item.ExternalId.ToString(),
					item.LongId,
					OptionalAt = item.OptionalAt?.ToString("O")
				});
		}

		var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
		await _keepAlive.ExecuteAsync(
			"""
			INSERT INTO DecimalItem (Id, Amount) VALUES (1, 1.5), (2, 1.5), (3, 2.0);
			INSERT INTO OffsetItem (Id, At) VALUES (1, @a1), (2, @a2), (3, @a3);
			""",
			new
			{
				a1 = t0.AddHours(2).ToString("O"),
				a2 = t0.AddHours(1).ToString("O"),
				a3 = t0.AddHours(3).ToString("O")
			});
	}

	public async Task DisposeAsync()
	{
		if (_keepAlive is not null)
		{
			await _keepAlive.DisposeAsync();
		}
	}
}

[CollectionDefinition("dapper-sqlite")]
public sealed class DapperSqliteCollection : ICollectionFixture<SqliteDapperFixture>;

[Collection("dapper-sqlite")]
public sealed class DapperPagingTests(SqliteDapperFixture fixture)
{
	private const string FilterSql = "SELECT Id, Name, Price, CreatedAt, Kind, ExternalId, LongId, OptionalAt FROM CatalogItem";

	[Fact]
	public async Task Forward_No_Overlap_Matches_Seed_Order()
	{
		using var conn = fixture.Open();
		var first = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 5), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		var second = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(first.Next, 5), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		Assert.Equal(CatalogSeed.Items.Take(5).Select(i => i.Id), first.Items.Select(i => i.Id));
		Assert.Equal(CatalogSeed.Items.Skip(5).Take(5).Select(i => i.Id), second.Items.Select(i => i.Id));
		Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
		Assert.True(second.HasPrevious);
	}

	[Fact]
	public async Task Previous_Restores_First_Page()
	{
		using var conn = fixture.Open();
		var first = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 5), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		var second = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(first.Next, 5), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		var back = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(second.Previous, 5), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		Assert.Equal(first.Items.Select(i => i.Id), back.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Enum_Price_Descending()
	{
		using var conn = fixture.Open();
		var page = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 5),
			CatalogSeed.For(ItemSortField.Price, SortDirection.Descending),
			FilterSql,
			SqlDialect.Sqlite);
		var expected = CatalogSeed.Items.OrderByDescending(i => i.Price).ThenBy(i => i.Id).Take(5).Select(i => i.Id);
		Assert.Equal(expected, page.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Missing_Sql_Throws()
	{
		using var conn = fixture.Open();
		var key = SortKey.For<CatalogItem>().ThenByUnique(x => x.Id);
		var ex = await Assert.ThrowsAsync<PaginationException>(
			async () => await conn.QueryCursorPageAsync<CatalogItem>(
				new CursorRequest(null, 5), key, FilterSql, SqlDialect.Sqlite));
		Assert.Equal(PaginationErrorCode.MissingSqlIdentifier, ex.Code);
	}

	[Fact]
	public async Task Shadow_Throws()
	{
		using var conn = fixture.Open();
		var key = SortKey.For<CatalogItem>().ByShadow<string>("DisplayCode", sql: "Name").ThenByUnique(x => x.Id, sql: "Id");
		var ex = await Assert.ThrowsAsync<PaginationException>(
			async () => await conn.QueryCursorPageAsync<CatalogItem>(
				new CursorRequest(null, 5), key, FilterSql, SqlDialect.Sqlite));
		Assert.Equal(PaginationErrorCode.ShadowNotSupported, ex.Code);
	}

	[Fact]
	public void Postgres_Tuple_Sql_When_Directions_Match()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ByPrice,
			FilterSql,
			SqlDialect.PostgreSql,
			[10d, 1],
			walkBackward: false,
			take: 6);
		Assert.Contains("(\"Price\", \"Id\") > (@ks0, @ks1)", sql, StringComparison.Ordinal);
		Assert.Contains("LIMIT @take", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void Postgres_Tuple_Three_Column_Sql()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ByPriceCreatedAt,
			FilterSql,
			SqlDialect.PostgreSql,
			[10d, CatalogSeed.T0.AddDays(1), 1],
			walkBackward: false,
			take: 6);
		Assert.Contains("(\"Price\", \"CreatedAt\", \"Id\") > (@ks0, @ks1, @ks2)", sql, StringComparison.Ordinal);
		Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void Postgres_Tuple_Four_Column_Sql()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ByNamePriceCreatedAt,
			FilterSql,
			SqlDialect.PostgreSql,
			["Item-A", 10d, CatalogSeed.T0.AddDays(1), 1],
			walkBackward: false,
			take: 6);
		// Name is a string slot — Dapper still emits tuple when directions match (host SQL assumes non-null).
		Assert.Contains("(\"Name\", \"Price\", \"CreatedAt\", \"Id\") > (@ks0, @ks1, @ks2, @ks3)", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void Postgres_Tuple_Nine_Column_Sql()
	{
		var first = CatalogSeed.Items[0];
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ByNineValueTypes,
			FilterSql,
			SqlDialect.PostgreSql,
			[first.Price, first.CreatedAt, first.LongId, first.Kind, first.ExternalId, first.VendorId, first.Flag, first.Rank, first.Id],
			walkBackward: false,
			take: 6);
		Assert.Contains("(\"Price\", \"CreatedAt\", \"LongId\", \"Kind\", \"ExternalId\", \"VendorId\", \"Flag\", \"Rank\", \"Id\") >", sql, StringComparison.Ordinal);
		Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void Postgres_OrderBy_Nulls_Last_In_Sql()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ByName,
			FilterSql,
			SqlDialect.PostgreSql,
			values: null,
			walkBackward: false,
			take: 6,
			new PaginationOptions { Nulls = NullOrder.Last });
		Assert.Contains("\"Name\" ASC NULLS LAST", sql, StringComparison.Ordinal);
		Assert.Contains("\"Id\" ASC NULLS LAST", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void Sqlite_OrderBy_Nulls_First_In_Sql()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ByName,
			FilterSql,
			SqlDialect.Sqlite,
			values: null,
			walkBackward: false,
			take: 6,
			new PaginationOptions { Nulls = NullOrder.First });
		Assert.Contains("\"Name\" ASC NULLS FIRST", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void SqlServer_OrderBy_Has_No_Nulls_Keyword()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ByName,
			FilterSql,
			SqlDialect.SqlServer,
			values: null,
			walkBackward: false,
			take: 6,
			new PaginationOptions { Nulls = NullOrder.Last });
		Assert.DoesNotContain("NULLS", sql, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Mixed_Direction_Is_Or_Not_Tuple()
	{
		var key = SortKey.For<CatalogItem>().ByDescending(x => x.Price, sql: "Price").ThenByUnique(x => x.Id, sql: "Id");
		var sql = DapperCursorExtensions.BuildSql(key, FilterSql, SqlDialect.PostgreSql, [10d, 1], walkBackward: false, take: 6);
		Assert.DoesNotContain("(\"Price\", \"Id\")", sql, StringComparison.Ordinal);
		Assert.Contains(" OR ", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void SqlServer_Uses_Fetch_Next()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ById, FilterSql, SqlDialect.SqlServer, values: null, walkBackward: false, take: 6);
		Assert.Contains("FETCH NEXT @take ROWS ONLY", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void QueryHint_None_Does_Not_Prefix_SqlServer()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ById, FilterSql, SqlDialect.SqlServer, values: null, walkBackward: false, take: 6);
		Assert.DoesNotContain("READ UNCOMMITTED", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("NOLOCK", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void QueryHint_ReadUncommitted_Prefixes_SqlServer_Only()
	{
		var hinted = new PaginationOptions { Hint = QueryHint.ReadUncommitted };
		var sqlServer = DapperCursorExtensions.BuildSql(
			CatalogSeed.ById, FilterSql, SqlDialect.SqlServer, values: null, walkBackward: false, take: 6, hinted);
		Assert.StartsWith("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;", sqlServer, StringComparison.Ordinal);
		Assert.Contains("FETCH NEXT @take ROWS ONLY", sqlServer, StringComparison.Ordinal);
		Assert.DoesNotContain("READ COMMITTED", sqlServer, StringComparison.Ordinal);

		var postgres = DapperCursorExtensions.BuildSql(
			CatalogSeed.ById, FilterSql, SqlDialect.PostgreSql, values: null, walkBackward: false, take: 6, hinted);
		Assert.DoesNotContain("READ UNCOMMITTED", postgres, StringComparison.Ordinal);
		Assert.DoesNotContain("NOLOCK", postgres, StringComparison.Ordinal);

		var sqlite = DapperCursorExtensions.BuildSql(
			CatalogSeed.ById, FilterSql, SqlDialect.Sqlite, values: null, walkBackward: false, take: 6, hinted);
		Assert.DoesNotContain("READ UNCOMMITTED", sqlite, StringComparison.Ordinal);
	}

	[Fact]
	public void QueryHint_ReadUncommitted_Prefixes_Count_Sql()
	{
		const string countSql = "SELECT COUNT(*) FROM (SELECT Id, Name, Price FROM CatalogItem) AS _bbp_count";
		var prefixed = QueryHintSql.Apply(QueryHint.ReadUncommitted, SqlDialect.SqlServer, countSql);
		Assert.StartsWith("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;", prefixed, StringComparison.Ordinal);
		Assert.Contains("SELECT COUNT(*)", prefixed, StringComparison.Ordinal);
		Assert.Equal(countSql, QueryHintSql.Apply(QueryHint.None, SqlDialect.SqlServer, countSql));
		Assert.Equal(countSql, QueryHintSql.Apply(QueryHint.ReadUncommitted, SqlDialect.PostgreSql, countSql));
		Assert.Equal(countSql, QueryHintSql.Apply(QueryHint.ReadUncommitted, SqlDialect.Sqlite, countSql));
	}

	[Fact]
	public void QueryHint_ReadUncommitted_Preserves_Host_Nolock()
	{
		const string host = "SELECT Id, Name, Price FROM products WITH (NOLOCK) WHERE Deleted = 0";
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ById,
			host,
			SqlDialect.SqlServer,
			values: null,
			walkBackward: false,
			take: 6,
			new PaginationOptions { Hint = QueryHint.ReadUncommitted });
		Assert.StartsWith("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;", sql, StringComparison.Ordinal);
		Assert.Contains("WITH (NOLOCK)", sql, StringComparison.Ordinal);
		Assert.DoesNotContain("READ COMMITTED", sql, StringComparison.Ordinal);
	}

	[Fact]
	public async Task QueryHint_ReadUncommitted_On_Sqlite_Execute_Is_Noop()
	{
		using var conn = fixture.Open();
		var page = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 5),
			CatalogSeed.ById,
			FilterSql,
			SqlDialect.Sqlite,
			options: new PaginationOptions { Hint = QueryHint.ReadUncommitted });
		Assert.Equal(5, page.Items.Count);
	}

	[Fact]
	public void Sqlite_Uses_Limit()
	{
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ById, FilterSql, SqlDialect.Sqlite, values: null, walkBackward: false, take: 6);
		Assert.Contains("LIMIT @take", sql, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Host_Where_Params_No_Collision()
	{
		using var conn = fixture.Open();
		var page = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 20),
			CatalogSeed.ById,
			FilterSql + " WHERE Price >= @minPrice",
			SqlDialect.Sqlite,
			new { minPrice = 20d },
			new PaginationOptions { IncludeTotalCount = true });
		Assert.All(page.Items, i => Assert.True(i.Price >= 20d));
		Assert.Equal(page.Items.Count, page.TotalCount);
	}

	[Theory]
	[MemberData(nameof(EnumMembers))]
	public async Task Enum_First_Page_Each_Field_Asc_And_Desc(ItemSortField field, SortDirection direction)
	{
		using var conn = fixture.Open();
		var key = CatalogSeed.For(field, direction);
		var page = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 5), key, FilterSql, SqlDialect.Sqlite);
		Assert.Equal(5, page.Items.Count);
		Assert.True(page.HasNext);
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

		Assert.Equal(expected.Take(5).Select(i => i.Id), page.Items.Select(i => i.Id));
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
	public async Task Duplicate_Prices_Stable()
	{
		using var conn = fixture.Open();
		var first = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 4), CatalogSeed.ByPrice, FilterSql, SqlDialect.Sqlite);
		var second = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(first.Next, 4), CatalogSeed.ByPrice, FilterSql, SqlDialect.Sqlite);
		var ids = first.Items.Concat(second.Items).Select(i => i.Id).ToList();
		Assert.Equal(ids.Distinct().Count(), ids.Count);
		Assert.Equal(
			CatalogSeed.Items.OrderBy(i => i.Price).ThenBy(i => i.Id).Take(4).Select(i => i.Id),
			first.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Clr_Guid_Long_Enum_Decimal_Offset()
	{
		using var conn = fixture.Open();
		var byGuid = SortKey.For<CatalogItem>().By(x => x.ExternalId, sql: "ExternalId").ThenByUnique(x => x.Id, sql: "Id");
		var byLong = SortKey.For<CatalogItem>().By(x => x.LongId, sql: "LongId").ThenByUnique(x => x.Id, sql: "Id");
		var byKind = SortKey.For<CatalogItem>().By(x => x.Kind, sql: "Kind").ThenByUnique(x => x.Id, sql: "Id");
		Assert.Equal(4, (await conn.QueryCursorPageAsync<CatalogItem>(new CursorRequest(null, 4), byGuid, FilterSql, SqlDialect.Sqlite)).Items.Count);
		Assert.Equal(4, (await conn.QueryCursorPageAsync<CatalogItem>(new CursorRequest(null, 4), byLong, FilterSql, SqlDialect.Sqlite)).Items.Count);
		Assert.Equal(4, (await conn.QueryCursorPageAsync<CatalogItem>(new CursorRequest(null, 4), byKind, FilterSql, SqlDialect.Sqlite)).Items.Count);

		var decimalKey = SortKey.For<DecimalItem>().By(x => x.Amount, sql: "Amount").ThenByUnique(x => x.Id, sql: "Id");
		var decimals = await conn.QueryCursorPageAsync<DecimalItem>(
			new CursorRequest(null, 2),
			decimalKey,
			"SELECT Id, Amount FROM DecimalItem",
			SqlDialect.Sqlite);
		Assert.Equal([1, 2], decimals.Items.Select(r => r.Id));

		var offsetKey = SortKey.For<OffsetItem>().By(x => x.At, sql: "At").ThenByUnique(x => x.Id, sql: "Id");
		var offsets = await conn.QueryCursorPageAsync<OffsetItem>(
			new CursorRequest(null, 2),
			offsetKey,
			"SELECT Id, At FROM OffsetItem",
			SqlDialect.Sqlite);
		Assert.Equal([2, 1], offsets.Items.Select(r => r.Id));
	}

	[Fact]
	public async Task IncludeTotalCount_Off()
	{
		using var conn = fixture.Open();
		var page = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 3), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		Assert.Null(page.TotalCount);
	}

	[Fact]
	public async Task Cancellation()
	{
		using var conn = fixture.Open();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			async () => await conn.QueryCursorPageAsync<CatalogItem>(
				new CursorRequest(null, 5), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite, cancellationToken: cts.Token));
	}

	[Fact]
	public void No_IQueryable_Or_ByShadow_On_Dapper_Api()
	{
		Assert.DoesNotContain(
			typeof(DapperCursorExtensions).GetMethods(),
			m => m.GetParameters().Any(p => p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(IQueryable<>)));
	}

	[Fact]
	public async Task Empty_And_Invalid_Cursor()
	{
		using var conn = fixture.Open();
		var empty = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 5),
			CatalogSeed.ById,
			FilterSql + " WHERE Id < 0",
			SqlDialect.Sqlite);
		Assert.Empty(empty.Items);
		await Assert.ThrowsAsync<PaginationException>(
			async () => await conn.QueryCursorPageAsync<CatalogItem>(
				new CursorRequest("not-a-valid-cursor", 5), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite));
	}

	[Fact]
	public void Host_Sql_OrderBy_Offset_Limit_Throws()
	{
		var ex = Assert.Throws<PaginationException>(
			() => DapperCursorExtensions.BuildSql(
				CatalogSeed.ById,
				FilterSql + " ORDER BY Id",
				SqlDialect.Sqlite,
				values: null,
				walkBackward: false,
				take: 6));
		Assert.Equal(PaginationErrorCode.InvalidHostSql, ex.Code);

		Assert.Equal(
			PaginationErrorCode.InvalidHostSql,
			Assert.Throws<PaginationException>(
				() => DapperCursorExtensions.BuildSql(
					CatalogSeed.ById, FilterSql + " LIMIT 5", SqlDialect.Sqlite, null, false, 6)).Code);

		Assert.Equal(
			PaginationErrorCode.InvalidHostSql,
			Assert.Throws<PaginationException>(
				() => DapperCursorExtensions.BuildSql(
					CatalogSeed.ById,
					"SELECT Id FROM CatalogItem OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY",
					SqlDialect.SqlServer,
					null,
					false,
					6)).Code);
	}

	[Fact]
	public void Host_Sql_Nolock_Hint_Is_Preserved_Inside_Subquery()
	{
		const string host = "SELECT Id, Name, Price FROM products WITH (NOLOCK) WHERE Deleted = 0";
		var sql = DapperCursorExtensions.BuildSql(
			CatalogSeed.ById, host, SqlDialect.SqlServer, values: null, walkBackward: false, take: 6);
		Assert.Contains("WITH (NOLOCK)", sql, StringComparison.Ordinal);
		Assert.Contains("SELECT * FROM (", sql, StringComparison.Ordinal);
		Assert.Contains("FETCH NEXT @take ROWS ONLY", sql, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Backward_Empty_Cursor_Is_Last_Page()
	{
		using var conn = fixture.Open();
		var page = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 5, PageDirection.Backward),
			CatalogSeed.ById,
			FilterSql,
			SqlDialect.Sqlite);
		Assert.Equal(CatalogSeed.Items.Select(i => i.Id).Skip(7).Take(5), page.Items.Select(i => i.Id));
		Assert.True(page.HasPrevious);
		Assert.False(page.HasNext);
	}

	[Fact]
	public async Task Replay_Same_Cursor_Is_Stable()
	{
		using var conn = fixture.Open();
		var first = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(null, 4), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		var a = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(first.Next, 4), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		var b = await conn.QueryCursorPageAsync<CatalogItem>(
			new CursorRequest(first.Next, 4), CatalogSeed.ById, FilterSql, SqlDialect.Sqlite);
		Assert.Equal(a.Items.Select(i => i.Id), b.Items.Select(i => i.Id));
	}
}

internal sealed class DecimalHandler : SqlMapper.TypeHandler<decimal>
{
	public override void SetValue(IDbDataParameter parameter, decimal value)
		=> parameter.Value = value;

	public override decimal Parse(object value)
		=> Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
{
	public override void SetValue(IDbDataParameter parameter, Guid value)
		=> parameter.Value = value.ToString();

	public override Guid Parse(object value)
		=> value is Guid g ? g : Guid.Parse((string)value);
}

internal sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
	public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
		=> parameter.Value = value.ToString("O");

	public override DateTimeOffset Parse(object value)
		=> DateTimeOffset.Parse((string)value, null, System.Globalization.DateTimeStyles.RoundtripKind);
}
