using System.Diagnostics;
using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MR.EntityFrameworkCore.KeysetPagination;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Benchmarks;

/// <summary>
/// One-shot keyset vs OFFSET vs MR at a chosen row count (not BenchmarkDotNet).
/// Usage: --probe 10000000
/// </summary>
internal static class LargeTableProbe
{
	public static void Run(int rows)
	{
		var path = BenchStore.EnsureFile(rows);
		Console.WriteLine($"Probe rows={rows:N0} file={path} (insert uses synchronous=OFF; queries use NORMAL)");
		Console.WriteLine("Libraries: FeatureFusion ToCursorPageAsync, EF OFFSET, MR.EntityFrameworkCore.KeysetPagination 1.5.0");
		Console.WriteLine("KB columns: mean managed allocations per page (this thread), after one warmup.");
		if (rows >= BenchStore.CatalogRows)
		{
			Console.WriteLine("100M catalog: OFFSET at mid/deep skip is expected to take a long time; that is the comparison.");
		}

		var skips = new[] { 0, Math.Max(1, rows / 10), Math.Max(1, rows / 2) };
		foreach (var skip in skips.Distinct())
		{
			BenchStore.AssertEquivalentIds(path, BenchSort.PriceId, skip);
			Console.WriteLine($"Verified IDs skip={skip:N0} (not timed)");
		}

		var lastFf = BenchStore.FeatureFusionBackwardIds(path, BenchSort.PriceId);
		var lastMr = BenchStore.MrBackwardIds(path, BenchSort.PriceId);
		var lastOff = BenchStore.OffsetLastPageIds(path, BenchSort.PriceId, rows);
		if (!lastFf.SequenceEqual(lastMr) || !lastFf.SequenceEqual(lastOff))
		{
			throw new InvalidOperationException("Probe backward correctness gate failed.");
		}

		using (var warm = BenchStore.Create(path))
		{
			_ = warm.Items.AsNoTracking().OrderBy(i => i.Price).ThenBy(i => i.Id).Take(BenchStore.Limit).ToList();
			_ = warm.Items.AsNoTracking()
				.ToCursorPageAsync(new CursorRequest(null, BenchStore.Limit), BenchStore.PriceIdKey)
				.GetAwaiter()
				.GetResult();
			_ = warm.Items.AsNoTracking()
				.KeysetPaginateQuery(BenchStore.MrPriceId)
				.Take(BenchStore.Limit)
				.ToList();
		}

		Console.WriteLine(
			$"{"Skip",12} {"OFFSET ms",12} {"OFFSET KB",12} {"FeatureFusion",16} {"FF KB",10} {"MR 1.5.0",12} {"MR KB",10}");
		foreach (var skip in skips.Distinct())
		{
			var cursor = BenchStore.CursorAtSkip(path, BenchSort.PriceId, skip);
			var reference = BenchStore.ReferenceAtSkip(path, BenchSort.PriceId, skip);

			var offset = TimeAndAlloc(() =>
			{
				using var db = BenchStore.Create(path);
				return db.Items.AsNoTracking()
					.OrderBy(i => i.Price)
					.ThenBy(i => i.Id)
					.Skip(skip)
					.Take(BenchStore.Limit)
					.ToList()
					.Count;
			});

			var keyset = TimeAndAlloc(() =>
			{
				using var db = BenchStore.Create(path);
				return db.Items.AsNoTracking()
					.ToCursorPageAsync(new CursorRequest(cursor, BenchStore.Limit), BenchStore.PriceIdKey)
					.GetAwaiter()
					.GetResult()
					.Items.Count;
			});

			var mr = TimeAndAlloc(() =>
			{
				using var db = BenchStore.Create(path);
				return db.Items.AsNoTracking()
					.KeysetPaginateQuery(BenchStore.MrPriceId, KeysetPaginationDirection.Forward, reference)
					.Take(BenchStore.Limit)
					.ToList()
					.Count;
			});

			Console.WriteLine(
				$"{skip,12:N0} {offset.Ms,12:0.0} {offset.AllocKb,12:0.0} {keyset.Ms,16:0.0} {keyset.AllocKb,10:0.0} {mr.Ms,12:0.0} {mr.AllocKb,10:0.0}");
		}

		var envPath = Environment.GetEnvironmentVariable("PAGINATION_PROBE_DB");
		if (string.IsNullOrWhiteSpace(envPath) && rows < 100_000)
		{
			try
			{
				File.Delete(path);
				File.Delete(path + "-wal");
				File.Delete(path + "-shm");
			}
			catch (IOException)
			{
				// temp file; ignore
			}
		}
	}

	/// <summary>
	/// One warmup, then 5 repeats. Time is mean wall-clock. Alloc is mean
	/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> delta (managed, this thread).
	/// </summary>
	private static (double Ms, double AllocKb) TimeAndAlloc(Func<int> action)
	{
		action();
		var before = GC.GetAllocatedBytesForCurrentThread();
		var sw = Stopwatch.StartNew();
		const int repeats = 5;
		for (var i = 0; i < repeats; i++)
		{
			_ = action();
		}

		sw.Stop();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		return (sw.Elapsed.TotalMilliseconds / repeats, allocated / (double)repeats / 1024.0);
	}
}
