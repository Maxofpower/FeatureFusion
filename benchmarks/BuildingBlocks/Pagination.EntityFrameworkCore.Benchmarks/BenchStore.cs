using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MR.EntityFrameworkCore.KeysetPagination;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Benchmarks;

public sealed class BenchItem
{
	public int Id { get; set; }
	public double Price { get; set; }
	public string Name { get; set; } = "";
	public DateTime CreatedAt { get; set; }
}

public sealed class BenchContext : DbContext
{
	public BenchContext(DbContextOptions<BenchContext> options) : base(options) { }

	public DbSet<BenchItem> Items => Set<BenchItem>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<BenchItem>().HasKey(i => i.Id);
		modelBuilder.Entity<BenchItem>().Property(i => i.Id).ValueGeneratedNever();
		modelBuilder.Entity<BenchItem>()
			.Property(i => i.CreatedAt)
			.HasConversion(
				v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)).ToUnixTimeSeconds(),
				v => DateTime.UnixEpoch.AddSeconds(v));
		modelBuilder.Entity<BenchItem>().HasIndex(i => new { i.Price, i.Id });
		modelBuilder.Entity<BenchItem>()
			.HasIndex(i => new { i.CreatedAt, i.Id })
			.IsDescending(true, true);
	}
}

public enum BenchSort
{
	PriceId,
	IdAsc,
	CreatedAtDesc
}

public enum BenchDepth
{
	First,
	Mid,
	Deep
}

/// <summary>
/// Shared file SQLite dataset for Default-job BDN and <c>--probe</c>.
/// Persist with <c>PAGINATION_PROBE_DB</c>. Row count: <c>PAGINATION_BENCH_ROWS</c> (BDN default 1_000_000).
/// </summary>
internal static class BenchStore
{
	public const int Limit = 20;
	public const int BdnDefaultRows = 1_000_000;
	public const int CatalogRows = 100_000_000;

	internal static readonly SortKey<BenchItem> PriceIdKey =
		SortKey.For<BenchItem>().By(x => x.Price).ThenByUnique(x => x.Id);

	internal static readonly SortKey<BenchItem> IdKey =
		SortKey.For<BenchItem>().ThenByUnique(x => x.Id);

	internal static readonly SortKey<BenchItem> CreatedAtDescKey =
		SortKey.For<BenchItem>().ByDescending(x => x.CreatedAt).ThenByUniqueDescending(x => x.Id);

	internal static readonly KeysetQueryDefinition<BenchItem> MrPriceId =
		KeysetQuery.Build<BenchItem>(b => b.Ascending(x => x.Price).Ascending(x => x.Id));

	internal static readonly KeysetQueryDefinition<BenchItem> MrId =
		KeysetQuery.Build<BenchItem>(b => b.Ascending(x => x.Id));

	internal static readonly KeysetQueryDefinition<BenchItem> MrCreatedAtDesc =
		KeysetQuery.Build<BenchItem>(b => b.Descending(x => x.CreatedAt).Descending(x => x.Id));

	public static int BdnRows()
	{
		var text = Environment.GetEnvironmentVariable("PAGINATION_BENCH_ROWS");
		return int.TryParse(text, out var n) && n > Limit ? n : BdnDefaultRows;
	}

	public static int SkipFor(int rows, BenchDepth depth)
		=> depth switch
		{
			BenchDepth.First => 0,
			BenchDepth.Mid => Math.Max(1, rows / 10),
			BenchDepth.Deep => Math.Max(1, rows / 2),
			_ => throw new ArgumentOutOfRangeException(nameof(depth))
		};

	public static string EnsureFile(int rows)
	{
		var path = Environment.GetEnvironmentVariable("PAGINATION_PROBE_DB");
		if (string.IsNullOrWhiteSpace(path))
		{
			path = Path.Combine(Path.GetTempPath(), $"pagination-bench-{rows}.db");
		}

		if (File.Exists(path) && CountRows(path) == rows)
		{
			return path;
		}

		EnsureFreeSpace(path, rows);

		foreach (var extra in new[] { path, path + "-wal", path + "-shm" })
		{
			if (File.Exists(extra))
			{
				File.Delete(extra);
			}
		}

		Console.WriteLine($"Creating {rows:N0}-row catalog at {path}");
		using var conn = new SqliteConnection("Data Source=" + path);
		conn.Open();
		using (var pragmas = conn.CreateCommand())
		{
			pragmas.CommandText = """
				PRAGMA journal_mode=WAL;
				PRAGMA synchronous=OFF;
				PRAGMA locking_mode=EXCLUSIVE;
				PRAGMA temp_store=MEMORY;
				PRAGMA cache_size=-1048576;
				CREATE TABLE Items (
					Id INTEGER PRIMARY KEY,
					Price REAL NOT NULL,
					Name TEXT NOT NULL,
					CreatedAt INTEGER NOT NULL);
				""";
			pragmas.ExecuteNonQuery();
		}

		const int chunk = 250_000;
		using var insert = conn.CreateCommand();
		insert.CommandText = """
			INSERT INTO Items (Id, Price, Name, CreatedAt)
			WITH RECURSIVE seq(i) AS (
				SELECT $lo
				UNION ALL
				SELECT i + 1 FROM seq WHERE i < $hi
			)
			SELECT i, 10.0 + (i % 50), '', i FROM seq;
			""";
		var pLo = insert.CreateParameter();
		pLo.ParameterName = "$lo";
		insert.Parameters.Add(pLo);
		var pHi = insert.CreateParameter();
		pHi.ParameterName = "$hi";
		insert.Parameters.Add(pHi);
		insert.Prepare();

		var load = System.Diagnostics.Stopwatch.StartNew();
		for (var lo = 1; lo <= rows; lo += chunk)
		{
			var hi = Math.Min(lo + chunk - 1, rows);
			pLo.Value = lo;
			pHi.Value = hi;
			insert.ExecuteNonQuery();
			if (lo == 1 || hi % 5_000_000 == 0 || hi == rows)
			{
				Console.WriteLine($"  inserted {hi:N0} / {rows:N0} in {load.Elapsed.TotalSeconds:0.0}s");
			}
		}

		Console.WriteLine("  building indexes…");
		using (var idx = conn.CreateCommand())
		{
			idx.CommandText = """
				CREATE INDEX IX_Items_Price_Id ON Items (Price, Id);
				CREATE INDEX IX_Items_CreatedAt_Id ON Items (CreatedAt DESC, Id DESC);
				PRAGMA locking_mode=NORMAL;
				PRAGMA synchronous=NORMAL;
				""";
			idx.ExecuteNonQuery();
		}

		load.Stop();
		Console.WriteLine($"Catalog ready: {rows:N0} rows in {load.Elapsed.TotalSeconds:0.0}s");
		return path;
	}

	public static BenchContext Create(string path)
	{
		var options = new DbContextOptionsBuilder<BenchContext>()
			.UseSqlite("Data Source=" + path)
			.Options;
		return new BenchContext(options);
	}

	public static SortKey<BenchItem> SortKeyFor(BenchSort sort)
		=> sort switch
		{
			BenchSort.PriceId => PriceIdKey,
			BenchSort.IdAsc => IdKey,
			BenchSort.CreatedAtDesc => CreatedAtDescKey,
			_ => throw new ArgumentOutOfRangeException(nameof(sort))
		};

	public static KeysetQueryDefinition<BenchItem> MrFor(BenchSort sort)
		=> sort switch
		{
			BenchSort.PriceId => MrPriceId,
			BenchSort.IdAsc => MrId,
			BenchSort.CreatedAtDesc => MrCreatedAtDesc,
			_ => throw new ArgumentOutOfRangeException(nameof(sort))
		};

	public static IQueryable<BenchItem> OffsetQuery(IQueryable<BenchItem> query, BenchSort sort)
		=> sort switch
		{
			BenchSort.PriceId => query.OrderBy(i => i.Price).ThenBy(i => i.Id),
			BenchSort.IdAsc => query.OrderBy(i => i.Id),
			BenchSort.CreatedAtDesc => query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id),
			_ => throw new ArgumentOutOfRangeException(nameof(sort))
		};

	public static BenchItem? ReferenceAtSkip(string path, BenchSort sort, int skip)
	{
		if (skip <= 0)
		{
			return null;
		}

		var rows = CountRows(path);
		var id = IdAtRank(sort, rows, skip);
		return ItemAtId(id);
	}

	public static string? CursorAtSkip(string path, BenchSort sort, int skip)
	{
		var edge = ReferenceAtSkip(path, sort, skip);
		if (edge is null)
		{
			return null;
		}

		var key = SortKeyFor(sort);
		object?[] values = sort switch
		{
			BenchSort.PriceId => [edge.Price, edge.Id],
			BenchSort.IdAsc => [edge.Id],
			BenchSort.CreatedAtDesc => [edge.CreatedAt, edge.Id],
			_ => throw new ArgumentOutOfRangeException(nameof(sort))
		};
		return CursorCodec.Encode(key, values, PageDirection.Forward, PaginationOptions.Default);
	}

	public static void AssertEquivalentIds(string path, BenchSort sort, int skip)
	{
		using var db = Create(path);
		var rows = CountRows(path);
		var expected = ExpectedIds(sort, rows, skip, Limit);

		var ff = db.Items.AsNoTracking()
			.ToCursorPageAsync(new CursorRequest(CursorAtSkip(path, sort, skip), Limit), SortKeyFor(sort))
			.GetAwaiter()
			.GetResult();
		var ffIds = ff.Items.Select(i => i.Id).ToList();

		var mrRef = ReferenceAtSkip(path, sort, skip);
		var mrIds = db.Items.AsNoTracking()
			.KeysetPaginateQuery(MrFor(sort), KeysetPaginationDirection.Forward, mrRef)
			.Take(Limit)
			.Select(i => i.Id)
			.ToList();

		if (!expected.SequenceEqual(ffIds) || !expected.SequenceEqual(mrIds))
		{
			throw new InvalidOperationException(
				$"Correctness gate: ID mismatch skip={skip} sort={sort}. expected=[{string.Join(',', expected)}] FF=[{string.Join(',', ffIds)}] MR=[{string.Join(',', mrIds)}]");
		}

		var offsetIds = OffsetQuery(db.Items.AsNoTracking(), sort)
			.Skip(skip)
			.Take(Limit)
			.Select(i => i.Id)
			.ToList();
		if (!expected.SequenceEqual(offsetIds))
		{
			throw new InvalidOperationException(
				$"Correctness gate: OFFSET mismatch skip={skip} sort={sort}.");
		}
	}

	public static IReadOnlyList<int> FeatureFusionBackwardIds(string path, BenchSort sort)
	{
		using var db = Create(path);
		var page = db.Items.AsNoTracking()
			.ToCursorPageAsync(new CursorRequest(null, Limit, PageDirection.Backward), SortKeyFor(sort))
			.GetAwaiter()
			.GetResult();
		return page.Items.Select(i => i.Id).ToList();
	}

	public static IReadOnlyList<int> MrBackwardIds(string path, BenchSort sort)
	{
		using var db = Create(path);
		var ctx = db.Items.AsNoTracking().KeysetPaginate(MrFor(sort), KeysetPaginationDirection.Backward);
		var items = ctx.Query.Take(Limit).ToList();
		ctx.EnsureCorrectOrder(items);
		return items.Select(i => i.Id).ToList();
	}

	public static IReadOnlyList<int> OffsetLastPageIds(string path, BenchSort sort, int rows)
		=> ExpectedIds(sort, rows, Math.Max(0, rows - Limit), Limit);

	private static List<int> ExpectedIds(BenchSort sort, int rows, int skip, int limit)
	{
		var count = Math.Min(limit, Math.Max(0, rows - skip));
		var ids = new List<int>(count);
		for (var r = skip + 1; r <= skip + count; r++)
		{
			ids.Add(IdAtRank(sort, rows, r));
		}

		return ids;
	}

	private static BenchItem ItemAtId(int id)
		=> new()
		{
			Id = id,
			Price = 10d + (id % 50),
			Name = "",
			CreatedAt = DateTime.UnixEpoch.AddSeconds(id)
		};

	/// <summary>1-based rank in the sort order → Id.</summary>
	private static int IdAtRank(BenchSort sort, int rows, int rank)
		=> sort switch
		{
			BenchSort.IdAsc => rank,
			BenchSort.CreatedAtDesc => rows - rank + 1,
			BenchSort.PriceId => IdAtPriceIdRank(rows, rank),
			_ => throw new ArgumentOutOfRangeException(nameof(sort))
		};

	private static int IdAtPriceIdRank(int rows, int rank)
	{
		var remaining = rank;
		for (var m = 0; m < 50; m++)
		{
			var first = m == 0 ? 50 : m;
			if (first > rows)
			{
				continue;
			}

			var count = ((rows - first) / 50) + 1;
			if (remaining <= count)
			{
				return first + ((remaining - 1) * 50);
			}

			remaining -= count;
		}

		throw new InvalidOperationException($"Price+Id rank {rank} is outside 1..{rows}.");
	}

	private static void EnsureFreeSpace(string path, int rows)
	{
		var root = Path.GetPathRoot(path);
		if (string.IsNullOrEmpty(root))
		{
			return;
		}

		var needed = (long)rows * 160L + (2L * 1024 * 1024 * 1024);
		var free = new DriveInfo(root).AvailableFreeSpace;
		if (free < needed)
		{
			throw new InvalidOperationException(
				$"Need ~{needed / (1024L * 1024 * 1024)} GB free on {root} for {rows:N0} rows (have {free / (1024L * 1024 * 1024)} GB). Set PAGINATION_PROBE_DB to a larger volume.");
		}
	}

	private static int CountRows(string path)
	{
		using var conn = new SqliteConnection("Data Source=" + path);
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Items'";
		if (Convert.ToInt32(cmd.ExecuteScalar()) != 1)
		{
			return -1;
		}

		cmd.CommandText = "SELECT MAX(Id) FROM Items";
		var max = cmd.ExecuteScalar();
		return max is null or DBNull ? -1 : Convert.ToInt32(max);
	}
}
