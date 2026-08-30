using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MR.EntityFrameworkCore.KeysetPagination;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Benchmarks;

public static class Program
{
	public static void Main(string[] args)
	{
		if (args is ["--probe", var n] && int.TryParse(n, out var rows) && rows > 0)
		{
			LargeTableProbe.Run(rows);
			return;
		}

		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
	}
}

/// <summary>
/// Cursor codec cost (unsigned vs HMAC). Independent of SQLite. Run with Default job only.
/// </summary>
[MemoryDiagnoser]
public class CursorCodecBenchmarks
{
	private SortKey<BenchItem> _key = null!;
	private PaginationOptions _unsigned = null!;
	private PaginationOptions _hmac = null!;
	private object?[] _values = null!;
	private string _unsignedCursor = null!;
	private string _hmacCursor = null!;

	[GlobalSetup]
	public void Setup()
	{
		_key = BenchStore.PriceIdKey;
		_unsigned = new PaginationOptions();
		_hmac = new PaginationOptions { SigningKey = "benchmark-signing-key-1"u8.ToArray() };
		_values = [15.5d, 42];
		_unsignedCursor = CursorCodec.Encode(_key, _values, PageDirection.Forward, _unsigned);
		_hmacCursor = CursorCodec.Encode(_key, _values, PageDirection.Forward, _hmac);
	}

	[Benchmark(Baseline = true)]
	public string Encode_Unsigned()
		=> CursorCodec.Encode(_key, _values, PageDirection.Forward, _unsigned);

	[Benchmark]
	public string Encode_Hmac()
		=> CursorCodec.Encode(_key, _values, PageDirection.Forward, _hmac);

	[Benchmark]
	public PageDirection Decode_Unsigned()
		=> CursorCodec.Decode(_unsignedCursor, _key, _unsigned).Walk;

	[Benchmark]
	public PageDirection Decode_Hmac()
		=> CursorCodec.Decode(_hmacCursor, _key, _hmac).Walk;
}

/// <summary>
/// File SQLite: OFFSET vs FeatureFusion vs MR.EntityFrameworkCore.KeysetPagination (1.5.0).
/// Default job. Default 1M rows (<c>PAGINATION_BENCH_ROWS</c>). Not Dry, not in-memory 5k.
/// </summary>
[MemoryDiagnoser]
public class KeysetLibraryBenchmarks
{
	[Params(BenchSort.PriceId, BenchSort.IdAsc, BenchSort.CreatedAtDesc)]
	public BenchSort Sort { get; set; }

	[Params(BenchDepth.First, BenchDepth.Mid, BenchDepth.Deep)]
	public BenchDepth Depth { get; set; }

	private string _path = null!;
	private int _skip;
	private string? _cursor;
	private BenchItem? _reference;

	[GlobalSetup]
	public void Setup()
	{
		var rows = BenchStore.BdnRows();
		_path = BenchStore.EnsureFile(rows);
		_skip = BenchStore.SkipFor(rows, Depth);
		_cursor = BenchStore.CursorAtSkip(_path, Sort, _skip);
		_reference = BenchStore.ReferenceAtSkip(_path, Sort, _skip);
		BenchStore.AssertEquivalentIds(_path, Sort, _skip);
	}

	[Benchmark(Baseline = true)]
	public async Task<int> Offset()
	{
		await using var db = BenchStore.Create(_path);
		var items = await BenchStore.OffsetQuery(db.Items.AsNoTracking(), Sort)
			.Skip(_skip)
			.Take(BenchStore.Limit)
			.ToListAsync();
		return items.Count;
	}

	[Benchmark]
	public async Task<int> FeatureFusion()
	{
		await using var db = BenchStore.Create(_path);
		var page = await db.Items.AsNoTracking()
			.ToCursorPageAsync(new CursorRequest(_cursor, BenchStore.Limit), BenchStore.SortKeyFor(Sort));
		return page.Items.Count;
	}

	[Benchmark]
	public async Task<int> MrKeyset()
	{
		await using var db = BenchStore.Create(_path);
		var items = await db.Items.AsNoTracking()
			.KeysetPaginateQuery(BenchStore.MrFor(Sort), KeysetPaginationDirection.Forward, _reference)
			.Take(BenchStore.Limit)
			.ToListAsync();
		return items.Count;
	}
}

/// <summary>Last page: FeatureFusion Backward vs MR Backward vs OFFSET Skip(N-20).</summary>
[MemoryDiagnoser]
public class KeysetBackwardBenchmarks
{
	[Params(BenchSort.PriceId, BenchSort.IdAsc, BenchSort.CreatedAtDesc)]
	public BenchSort Sort { get; set; }

	private string _path = null!;
	private int _rows;

	[GlobalSetup]
	public void Setup()
	{
		_rows = BenchStore.BdnRows();
		_path = BenchStore.EnsureFile(_rows);
		var ff = BenchStore.FeatureFusionBackwardIds(_path, Sort);
		var mr = BenchStore.MrBackwardIds(_path, Sort);
		var offset = BenchStore.OffsetLastPageIds(_path, Sort, _rows);
		if (!ff.SequenceEqual(mr) || !ff.SequenceEqual(offset))
		{
			throw new InvalidOperationException(
				$"Backward correctness gate failed for {Sort}. FF=[{string.Join(',', ff)}] MR=[{string.Join(',', mr)}] OFFSET=[{string.Join(',', offset)}]");
		}
	}

	[Benchmark(Baseline = true)]
	public async Task<int> Offset_LastPage()
	{
		await using var db = BenchStore.Create(_path);
		var items = await BenchStore.OffsetQuery(db.Items.AsNoTracking(), Sort)
			.Skip(Math.Max(0, _rows - BenchStore.Limit))
			.Take(BenchStore.Limit)
			.ToListAsync();
		return items.Count;
	}

	[Benchmark]
	public async Task<int> FeatureFusion_Backward()
	{
		await using var db = BenchStore.Create(_path);
		var page = await db.Items.AsNoTracking()
			.ToCursorPageAsync(
				new CursorRequest(null, BenchStore.Limit, PageDirection.Backward),
				BenchStore.SortKeyFor(Sort));
		return page.Items.Count;
	}

	[Benchmark]
	public async Task<int> Mr_Backward()
	{
		await using var db = BenchStore.Create(_path);
		var ctx = db.Items.AsNoTracking()
			.KeysetPaginate(BenchStore.MrFor(Sort), KeysetPaginationDirection.Backward);
		var items = await ctx.Query.Take(BenchStore.Limit).ToListAsync();
		ctx.EnsureCorrectOrder(items);
		return items.Count;
	}
}

/// <summary>Library overhead: compile seek+order to SQL text (no row fetch).</summary>
[MemoryDiagnoser]
public class KeysetOverheadBenchmarks
{
	private string _path = null!;
	private BenchItem _reference = null!;
	private string _cursor = null!;

	[GlobalSetup]
	public void Setup()
	{
		_path = BenchStore.EnsureFile(Math.Min(10_000, BenchStore.BdnRows()));
		_reference = BenchStore.ReferenceAtSkip(_path, BenchSort.PriceId, 20)!;
		_cursor = BenchStore.CursorAtSkip(_path, BenchSort.PriceId, 20)!;
	}

	[Benchmark(Baseline = true)]
	public string Offset_ToQueryString()
	{
		using var db = BenchStore.Create(_path);
		return BenchStore.OffsetQuery(db.Items.AsNoTracking(), BenchSort.PriceId)
			.Skip(20)
			.Take(BenchStore.Limit)
			.ToQueryString();
	}

	[Benchmark]
	public string FeatureFusion_ToQueryString()
	{
		using var db = BenchStore.Create(_path);
		return EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsNoTracking(),
			BenchStore.PriceIdKey,
			[_reference.Price, _reference.Id],
			walkBackward: false);
	}

	[Benchmark]
	public string Mr_ToQueryString()
	{
		using var db = BenchStore.Create(_path);
		return db.Items.AsNoTracking()
			.KeysetPaginateQuery(BenchStore.MrPriceId, KeysetPaginationDirection.Forward, _reference)
			.Take(BenchStore.Limit)
			.ToQueryString();
	}
}
