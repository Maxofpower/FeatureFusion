using BuildingBlocks.Pagination.TestSupport;
using Xunit;

namespace BuildingBlocks.Pagination.Tests;

public sealed class TypesErrorsAndThreadingTests
{
	[Fact]
	public void SortKey_Rejects_Complex_ValueObject_ByteArray_And_TypedId()
	{
		Assert.Equal(
			PaginationErrorCode.UnsupportedSortType,
			Assert.Throws<PaginationException>(
				() => SortKey.For<TypedRow>().By(x => x.Price).ThenByUnique(x => x.Id)).Code);

		Assert.Equal(
			PaginationErrorCode.UnsupportedSortType,
			Assert.Throws<PaginationException>(
				() => SortKey.For<TypedRow>().By(x => x.Payload).ThenByUnique(x => x.Id)).Code);

		Assert.Equal(
			PaginationErrorCode.UnsupportedSortType,
			Assert.Throws<PaginationException>(
				() => SortKey.For<TypedRow>().By(x => x.TypedId).ThenByUnique(x => x.Id)).Code);

		Assert.Equal(
			PaginationErrorCode.UnsupportedSortType,
			Assert.Throws<PaginationException>(
				() => SortKey.For<CatalogItem>().By(x => x.Vendor!).ThenByUnique(x => x.Id)).Code);
	}

	[Fact]
	public void SortKey_Accepts_Nested_ValueObject_Scalar_And_TypedId_Value()
	{
		var money = SortKey.For<TypedRow>().By(x => x.Price.Amount).ThenByUnique(x => x.Id);
		var id = SortKey.For<TypedRow>().By(x => x.TypedId.Value).ThenByUnique(x => x.Id);
		Assert.False(string.IsNullOrWhiteSpace(money.Fingerprint));
		Assert.NotEqual(money.Fingerprint, id.Fingerprint);
	}

	[Fact]
	public void SortKey_Accepts_Bool_DateOnly_TimeOnly_Enum()
	{
		_ = SortKey.For<TypedRow>().By(x => x.Flag).ThenByUnique(x => x.Id);
		_ = SortKey.For<TypedRow>().By(x => x.Day).ThenByUnique(x => x.Id);
		_ = SortKey.For<TypedRow>().By(x => x.Clock).ThenByUnique(x => x.Id);
		_ = SortKey.For<TypedRow>().By(x => x.Duration).ThenByUnique(x => x.Id);
		_ = SortKey.For<TypedRow>().By(x => x.Kind).ThenByUnique(x => x.Id);
		_ = SortKey.For<TypedRow>().By(x => x.ShortId).ThenByUnique(x => x.Id);
		_ = SortKey.For<TypedRow>().By(x => x.Tiny).ThenByUnique(x => x.Id);
		_ = SortKey.For<TypedRow>().By(x => x.Ratio).ThenByUnique(x => x.Id);
	}

	[Fact]
	public void SortKey_Rejects_Nullable_Value_Types()
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
	public void Codec_Roundtrip_Enum_Bool_DateOnly()
	{
		var key = SortKey.For<TypedRow>().By(x => x.Kind).ThenByUnique(x => x.Id);
		var encoded = CursorCodec.Encode(key, [ItemKind.B, 4], PageDirection.Forward, PaginationOptions.Default);
		var decoded = CursorCodec.Decode(encoded, key, PaginationOptions.Default);
		Assert.Equal(ItemKind.B, decoded.Values[0]);
		Assert.Equal(4, Convert.ToInt32(decoded.Values[1]));

		var boolKey = SortKey.For<TypedRow>().By(x => x.Flag).ThenByUnique(x => x.Id);
		var boolCursor = CursorCodec.Encode(boolKey, [true, 2], PageDirection.Forward, PaginationOptions.Default);
		Assert.Equal(true, CursorCodec.Decode(boolCursor, boolKey, PaginationOptions.Default).Values[0]);

		var day = new DateOnly(2024, 6, 1);
		var dayKey = SortKey.For<TypedRow>().By(x => x.Day).ThenByUnique(x => x.Id);
		var dayCursor = CursorCodec.Encode(dayKey, [day, 3], PageDirection.Forward, PaginationOptions.Default);
		Assert.Equal(day, CursorCodec.Decode(dayCursor, dayKey, PaginationOptions.Default).Values[0]);
	}

	[Fact]
	public void Codec_DateTime_Unspecified_And_Local_Are_Utc()
	{
		var key = SortKey.For<CatalogItem>().By(x => x.CreatedAt).ThenByUnique(x => x.Id);
		var unspecified = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Unspecified);
		var decoded = CursorCodec.Decode(
			CursorCodec.Encode(key, [unspecified, 1], PageDirection.Forward, PaginationOptions.Default),
			key,
			PaginationOptions.Default);
		var dt = Assert.IsType<DateTime>(decoded.Values[0]);
		Assert.Equal(DateTimeKind.Utc, dt.Kind);
		Assert.Equal(unspecified, dt);

		var local = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local);
		var localDecoded = CursorCodec.Decode(
			CursorCodec.Encode(key, [local, 1], PageDirection.Forward, PaginationOptions.Default),
			key,
			PaginationOptions.Default);
		Assert.Equal(DateTimeKind.Utc, Assert.IsType<DateTime>(localDecoded.Values[0]).Kind);
	}

	[Fact]
	public void Codec_Enum_Token_Has_No_Assembly_Name()
	{
		var key = SortKey.For<TypedRow>().By(x => x.Kind).ThenByUnique(x => x.Id);
		var encoded = CursorCodec.Encode(key, [ItemKind.B, 4], PageDirection.Forward, PaginationOptions.Default);
		var json = System.Text.Encoding.UTF8.GetString(
			Convert.FromBase64String(PadBase64(encoded.Split('.')[1].Replace('-', '+').Replace('_', '/'))));
		Assert.Contains("\"t\":\"enum\"", json, StringComparison.Ordinal);
		Assert.DoesNotContain("Assembly", json, StringComparison.Ordinal);
		Assert.Equal(ItemKind.B, CursorCodec.Decode(encoded, key, PaginationOptions.Default).Values[0]);
	}

	private static string PadBase64(string text)
	{
		return (text.Length % 4) switch
		{
			2 => text + "==",
			3 => text + "=",
			_ => text
		};
	}

	[Fact]
	public void Default_Options_Are_Not_A_Shared_Mutable_Singleton()
	{
		var a = PaginationOptions.Default;
		a.MaxLimit = 7;
		a.IncludeTotalCount = true;
		Assert.Equal(100, PaginationOptions.Default.MaxLimit);
		Assert.False(PaginationOptions.Default.IncludeTotalCount);
		Assert.Equal(QueryHint.None, PaginationOptions.Default.Hint);
	}

	[Fact]
	public void Concurrent_Encode_Decode_Is_Stable()
	{
		var key = CatalogSeed.ByPrice;
		var options = PaginationOptions.Default;
		Exception? fault = null;
		Parallel.For(0, 200, i =>
		{
			try
			{
				var encoded = CursorCodec.Encode(key, [10d + (i % 5), i], PageDirection.Forward, options);
				var decoded = CursorCodec.Decode(encoded, key, options);
				if (decoded.Values.Length != 2)
				{
					throw new InvalidOperationException("arity");
				}
			}
			catch (Exception ex)
			{
				Interlocked.CompareExchange(ref fault, ex, null);
			}
		});

		Assert.Null(fault);
	}

	[Fact]
	public void PaginationException_Preserves_Inner_And_Code()
	{
		var inner = new FormatException("bad");
		var ex = new PaginationException(PaginationErrorCode.InvalidCursor, "wrapped", inner);
		Assert.Equal(PaginationErrorCode.InvalidCursor, ex.Code);
		Assert.Same(inner, ex.InnerException);
	}

	[Fact]
	public void Whitespace_Cursor_Is_Empty()
	{
		Assert.True(CursorCodec.IsEmpty("   "));
		CursorCodec.Validate("\t", CatalogSeed.ById);
	}
}
