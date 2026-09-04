using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using BuildingBlocks.Pagination.EntityFrameworkCore.Query.Internal;
using BuildingBlocks.Pagination.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Tests;

[Collection("sqlite")]
public sealed class MultiColumnEfTests(SqliteFixture fixture)
{
	[Fact]
	public async Task Three_Column_Forward_Next_Previous()
	{
		await using var db = fixture.CreateContext();
		var key = CatalogSeed.ByPriceCreatedAt;
		var expected = CatalogSeed.Items
			.OrderBy(i => i.Price)
			.ThenBy(i => i.CreatedAt)
			.ThenBy(i => i.Id)
			.Select(i => i.Id)
			.ToList();

		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), key);
		Assert.Equal(expected.Take(4), first.Items.Select(i => i.Id));
		Assert.True(first.HasNext);

		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), key);
		Assert.Equal(expected.Skip(4).Take(4), second.Items.Select(i => i.Id));
		Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));

		var restored = await db.Items.ToCursorPageAsync(new CursorRequest(second.Previous, 4), key);
		Assert.Equal(first.Items.Select(i => i.Id), restored.Items.Select(i => i.Id));
	}

	[Fact]
	public async Task Four_Column_Forward_Next_No_Overlap()
	{
		await using var db = fixture.CreateContext();
		var key = CatalogSeed.ByNamePriceCreatedAt;
		var expected = CatalogSeed.Items
			.OrderBy(i => i.Name, StringComparer.Ordinal)
			.ThenBy(i => i.Price)
			.ThenBy(i => i.CreatedAt)
			.ThenBy(i => i.Id)
			.Select(i => i.Id)
			.ToList();

		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 5), key);
		Assert.Equal(expected.Take(5), first.Items.Select(i => i.Id));
		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 5), key);
		Assert.Equal(expected.Skip(5).Take(5), second.Items.Select(i => i.Id));
		Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
	}

	[Fact]
	public async Task Three_Column_Seek_Query_Is_Or_Chain_On_Sqlite()
	{
		await using var db = fixture.CreateContext();
		var key = CatalogSeed.ByPriceCreatedAt;
		var first = CatalogSeed.Items.OrderBy(i => i.Price).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id).First();
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			key,
			[first.Price, first.CreatedAt, first.Id],
			walkBackward: false);
		Assert.Contains(" OR ", sql, StringComparison.Ordinal);
		Assert.Contains("BuildingBlocks.Pagination:", sql, StringComparison.Ordinal);
	}

	[Fact]
	public void Mixed_Direction_Three_Column_Not_Tuple_Eligible()
	{
		var key = SortKey.For<CatalogItem>()
			.ByDescending(x => x.Price)
			.ThenBy(x => x.CreatedAt)
			.ThenByUnique(x => x.Id);
		Assert.False(SeekOps.TupleEligible(key, walkBackward: false));
		Assert.True(SeekOps.TupleSlotsNonNull(key));
	}

	[Fact]
	public void TupleSlotsNonNull_Rejects_String()
	{
		Assert.False(SeekOps.TupleSlotsNonNull(CatalogSeed.ByName));
		Assert.True(SeekOps.TupleSlotsNonNull(CatalogSeed.ByPrice));
		Assert.True(SeekOps.TupleSlotsNonNull(CatalogSeed.ByPriceCreatedAt));
		Assert.True(SeekOps.TupleSlotsNonNull(CatalogSeed.ByNineValueTypes));
	}

	[Fact]
	public async Task Nine_Column_Forward_Next_No_Overlap()
	{
		await using var db = fixture.CreateContext();
		var key = CatalogSeed.ByNineValueTypes;
		var expected = CatalogSeed.Items
			.OrderBy(i => i.Price)
			.ThenBy(i => i.CreatedAt)
			.ThenBy(i => i.LongId)
			.ThenBy(i => i.Kind)
			.ThenBy(i => i.ExternalId)
			.ThenBy(i => i.VendorId)
			.ThenBy(i => i.Flag)
			.ThenBy(i => i.Rank)
			.ThenBy(i => i.Id)
			.Select(i => i.Id)
			.ToList();

		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), key);
		Assert.Equal(expected.Take(4), first.Items.Select(i => i.Id));
		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), key);
		Assert.Equal(expected.Skip(4).Take(4), second.Items.Select(i => i.Id));
		Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
	}

	[Fact]
	public void Nine_Column_Value_Types_Are_Tuple_Eligible()
	{
		Assert.True(SeekOps.TupleEligible(CatalogSeed.ByNineValueTypes, walkBackward: false));
		Assert.True(SeekOps.TupleSlotsNonNull(CatalogSeed.ByNineValueTypes));
		Assert.True(CursorSeekTuple.CanUse(CatalogSeed.ByNineValueTypes, walkBackward: false));
		Assert.Equal(9, CatalogSeed.ByNineValueTypes.Slots.Count);
	}
}
