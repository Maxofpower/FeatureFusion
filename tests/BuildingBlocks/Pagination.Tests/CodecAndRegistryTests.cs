using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.TestSupport;
using Xunit;

namespace BuildingBlocks.Pagination.Tests;

public sealed class CodecTests
{
	[Fact]
	public void Codec_Roundtrip_Preserves_Values_And_Walk()
	{
		var options = PaginationOptions.Default;
		var encoded = CursorCodec.Encode(
			CatalogSeed.ByPrice,
			[15d, 2],
			PageDirection.Forward,
			options);
		var decoded = CursorCodec.Decode(encoded, CatalogSeed.ByPrice, options);
		Assert.Equal(PageDirection.Forward, decoded.Walk);
		Assert.Equal(15d, decoded.Values[0]);
		Assert.Equal(2, Convert.ToInt32(decoded.Values[1]));
	}

	[Fact]
	public void Codec_Fingerprint_Mismatch_Price_Then_Name()
	{
		var encoded = CursorCodec.Encode(CatalogSeed.ByPrice, [10d, 1], PageDirection.Forward, PaginationOptions.Default);
		var ex = Assert.Throws<PaginationException>(
			() => CursorCodec.Decode(encoded, CatalogSeed.ByName, PaginationOptions.Default));
		Assert.Equal(PaginationErrorCode.CursorSortMismatch, ex.Code);
	}

	[Fact]
	public void Codec_Hmac_Rejects_Unsigned_When_Key_Set()
	{
		var unsigned = CursorCodec.Encode(CatalogSeed.ById, [1], PageDirection.Forward, PaginationOptions.Default);
		var signedOptions = new PaginationOptions { SigningKey = "super-secret-key-1"u8.ToArray() };
		var ex = Assert.Throws<PaginationException>(
			() => CursorCodec.Decode(unsigned, CatalogSeed.ById, signedOptions));
		Assert.Equal(PaginationErrorCode.InvalidCursor, ex.Code);
	}

	[Fact]
	public void Codec_Hmac_Roundtrip_And_Tamper()
	{
		var options = new PaginationOptions { SigningKey = "super-secret-key-1"u8.ToArray() };
		var encoded = CursorCodec.Encode(CatalogSeed.ById, [3], PageDirection.Backward, options);
		var decoded = CursorCodec.Decode(encoded, CatalogSeed.ById, options);
		Assert.Equal(3, Convert.ToInt32(decoded.Values[0]));

		var tampered = encoded[..^1] + (encoded[^1] == 'A' ? 'B' : 'A');
		Assert.Throws<PaginationException>(() => CursorCodec.Decode(tampered, CatalogSeed.ById, options));
	}

	[Fact]
	public void Codec_Empty_SigningKey_Fails_Fast()
	{
		var options = new PaginationOptions { SigningKey = [] };
		var ex = Assert.Throws<PaginationException>(
			() => CursorCodec.Encode(CatalogSeed.ById, [1], PageDirection.Forward, options));
		Assert.Equal(PaginationErrorCode.SigningKeyRequired, ex.Code);
	}

	[Fact]
	public void Codec_Invalid_Format()
	{
		Assert.Throws<PaginationException>(
			() => CursorCodec.Decode("not-a-valid-cursor", CatalogSeed.ById, PaginationOptions.Default));
		Assert.False(CursorCodec.TryValidateFormat("%%%"));
		Assert.True(CursorCodec.IsEmpty(null));
		Assert.True(CursorCodec.TryValidateFormat(null));
	}

	[Fact]
	public void Codec_FromCursorValue_Bad_Json_Is_InvalidCursor()
	{
		var encoded = CursorCodec.Encode(CatalogSeed.ById, [1], PageDirection.Forward, PaginationOptions.Default);
		var body = encoded.Split('.')[1];
		var json = System.Text.Encoding.UTF8.GetString(Base64Url.Decode(body));
		var mutated = json.Replace("\"t\":\"System.Int32\",\"v\":1", "\"t\":\"System.Int32\",\"v\":\"nope\"", StringComparison.Ordinal);
		Assert.NotEqual(json, mutated);
		var bad = "v1." + Base64Url.Encode(System.Text.Encoding.UTF8.GetBytes(mutated));
		var ex = Assert.Throws<PaginationException>(
			() => CursorCodec.Decode(bad, CatalogSeed.ById, PaginationOptions.Default));
		Assert.Equal(PaginationErrorCode.InvalidCursor, ex.Code);
		Assert.NotNull(ex.InnerException);
	}
	[Fact]
	public void Codec_Roundtrip_Three_And_Four_Column_Keys()
	{
		var options = PaginationOptions.Default;
		var three = CursorCodec.Encode(
			CatalogSeed.ByPriceCreatedAt,
			[15d, CatalogSeed.T0.AddDays(2), 2],
			PageDirection.Forward,
			options);
		var threeDecoded = CursorCodec.Decode(three, CatalogSeed.ByPriceCreatedAt, options);
		Assert.Equal(3, threeDecoded.Values.Length);
		Assert.Equal(15d, threeDecoded.Values[0]);
		Assert.Equal(2, Convert.ToInt32(threeDecoded.Values[2]));

		var four = CursorCodec.Encode(
			CatalogSeed.ByNamePriceCreatedAt,
			["Item-A", 10d, CatalogSeed.T0.AddDays(1), 1],
			PageDirection.Backward,
			options);
		var fourDecoded = CursorCodec.Decode(four, CatalogSeed.ByNamePriceCreatedAt, options);
		Assert.Equal(PageDirection.Backward, fourDecoded.Walk);
		Assert.Equal(4, fourDecoded.Values.Length);
		Assert.Equal("Item-A", fourDecoded.Values[0]);
	}

	[Fact]
	public void Codec_Roundtrip_Nine_Column_Key()
	{
		var first = CatalogSeed.Items[0];
		var encoded = CursorCodec.Encode(
			CatalogSeed.ByNineValueTypes,
			[first.Price, first.CreatedAt, first.LongId, first.Kind, first.ExternalId, first.VendorId, first.Flag, first.Rank, first.Id],
			PageDirection.Forward,
			PaginationOptions.Default);
		var decoded = CursorCodec.Decode(encoded, CatalogSeed.ByNineValueTypes, PaginationOptions.Default);
		Assert.Equal(9, decoded.Values.Length);
		Assert.Equal(first.Id, Convert.ToInt32(decoded.Values[8]));
		Assert.Equal(first.Flag, Convert.ToByte(decoded.Values[6]));
	}
}

public sealed class SeekOpsTests
{
	[Fact]
	public void TupleEligible_True_For_Uniform_Directions()
	{
		Assert.True(SeekOps.TupleEligible(CatalogSeed.ByPrice, walkBackward: false));
		Assert.True(SeekOps.TupleEligible(CatalogSeed.ByPriceCreatedAt, walkBackward: false));
		Assert.True(SeekOps.TupleEligible(
			SortKey.For<CatalogItem>().ByDescending(x => x.Price).ThenByUniqueDescending(x => x.Id),
			walkBackward: false));
	}

	[Fact]
	public void TupleEligible_False_For_Mixed_Directions()
	{
		// Price DESC + Id ASC (lab PriceDesc pattern) is mixed — not a single row comparison.
		Assert.False(SeekOps.TupleEligible(CatalogSeed.ByPriceDesc, walkBackward: false));
		var mixed = SortKey.For<CatalogItem>()
			.ByDescending(x => x.Price)
			.ThenBy(x => x.CreatedAt)
			.ThenByUnique(x => x.Id);
		Assert.False(SeekOps.TupleEligible(mixed, walkBackward: false));
	}

	[Fact]
	public void TupleSlotsNonNull_Matrix()
	{
		Assert.True(SeekOps.TupleSlotsNonNull(CatalogSeed.ByPrice));
		Assert.True(SeekOps.TupleSlotsNonNull(CatalogSeed.ByPriceCreatedAt));
		Assert.False(SeekOps.TupleSlotsNonNull(CatalogSeed.ByName));
		Assert.False(SeekOps.TupleSlotsNonNull(CatalogSeed.ByNamePriceCreatedAt));
		Assert.True(SeekOps.TupleSlotsNonNull(CatalogSeed.ByNineValueTypes));
		Assert.Equal(9, CatalogSeed.ByNineValueTypes.Slots.Count);
		Assert.True(SeekOps.TupleEligible(CatalogSeed.ByNineValueTypes, walkBackward: false));
	}
}

public sealed class SortKeyAndRegistryTests
{
	[Fact]
	public void Registry_IsComplete_For_ItemSortField()
	{
		CatalogSeed.Registry.EnsureComplete();
		Assert.True(CatalogSeed.Registry.IsComplete());
		foreach (var field in Enum.GetValues<ItemSortField>())
		{
			Assert.True(CatalogSeed.Registry.TryGet(field, out _));
		}
	}

	[Fact]
	public void Registry_Unknown_Enum_TryGet_False()
	{
		var incomplete = new SortKeyRegistry<ItemSortField, CatalogItem>()
			.Add(ItemSortField.Id, CatalogSeed.ById);
		Assert.False(incomplete.IsComplete());
		Assert.False(incomplete.TryGet(ItemSortField.Name, out _));
		var miss = Assert.Throws<InvalidOperationException>(() => incomplete.Get(ItemSortField.Price));
		Assert.Contains("No SortKey is registered", miss.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("cursor", miss.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void SqlIdentifier_Rejects_Injection()
	{
		var ex = Assert.Throws<PaginationException>(
			() => SortKey.For<CatalogItem>().By(x => x.Name, sql: "Name; DROP TABLE").ThenByUnique(x => x.Id, sql: "Id"));
		Assert.Equal(PaginationErrorCode.InvalidIdentifier, ex.Code);
	}

	[Fact]
	public void Request_Invalid_Limit()
	{
		var ex = Assert.Throws<PaginationException>(
			() => RequestCursor.Resolve(new CursorRequest(null, 0), CatalogSeed.ById, PaginationOptions.Default));
		Assert.Equal(PaginationErrorCode.InvalidLimit, ex.Code);

		ex = Assert.Throws<PaginationException>(
			() => RequestCursor.Resolve(new CursorRequest(null, 101), CatalogSeed.ById, PaginationOptions.Default));
		Assert.Equal(PaginationErrorCode.InvalidLimit, ex.Code);
	}

	[Fact]
	public void CursorRequest_Has_No_PageIndex()
	{
		Assert.Null(typeof(CursorRequest).GetProperty("PageIndex"));
		Assert.Null(typeof(CursorRequest).GetProperty("pageIndex"));
	}

	[Fact]
	public void Validate_Empty_Succeeds_Mismatch_Throws()
	{
		CursorCodec.Validate(null, CatalogSeed.ById);
		CursorCodec.Validate("  ", CatalogSeed.ById);
		var encoded = CursorCodec.Encode(CatalogSeed.ByPrice, [10d, 1], PageDirection.Forward, PaginationOptions.Default);
		var ex = Assert.Throws<PaginationException>(
			() => CursorCodec.Validate(encoded, CatalogSeed.ByName));
		Assert.Equal(PaginationErrorCode.CursorSortMismatch, ex.Code);
	}
}
