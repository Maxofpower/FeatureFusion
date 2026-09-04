using System.Diagnostics;
using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using BuildingBlocks.Pagination.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Testcontainers.PostgreSql;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
	private PostgreSqlContainer? _container;

	public bool Ready { get; private set; }

	public string ConnectionString => _container?.GetConnectionString()
		?? throw new InvalidOperationException("PostgreSQL Testcontainer is not started.");

	/// <summary>GitHub Actions or an explicit required flag. Skips become failures.</summary>
	public static bool IsRequiredEnvironment =>
		string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(Environment.GetEnvironmentVariable("PAGINATION_POSTGRES_REQUIRED"), "true", StringComparison.OrdinalIgnoreCase)
		|| Environment.GetEnvironmentVariable("PAGINATION_POSTGRES_REQUIRED") == "1";

	public static bool CanRun => DockerAvailable();

	public async Task InitializeAsync()
	{
		if (IsRequiredEnvironment && !DockerAvailable())
		{
			throw new InvalidOperationException(
				"PostgreSQL CI requires Docker so Testcontainers can start postgres:16-alpine. Skipped tests are a CI failure.");
		}

		if (!CanRun)
		{
			Ready = false;
			return;
		}

		try
		{
			_container = new PostgreSqlBuilder()
				.WithImage("postgres:16-alpine")
				.Build();
			await _container.StartAsync();
			await using var db = CreateContextCore();
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
					VendorId = item.VendorId,
					Flag = item.Flag,
					Rank = item.Rank
				});
			}

			await db.SaveChangesAsync();
			Ready = true;
		}
		catch (Exception) when (!IsRequiredEnvironment)
		{
			Ready = false;
		}
	}

	public async Task DisposeAsync()
	{
		if (Ready && _container is not null)
		{
			await _container.DisposeAsync();
		}
	}

	public CatalogContext CreateContext(DbCommandInterceptor? extra = null)
	{
		if (!Ready)
		{
			throw new InvalidOperationException(
				"PostgreSQL Testcontainer is not ready. Use [PostgresFact] so tests skip when Docker is unavailable.");
		}

		return CreateContextCore(extra);
	}

	private CatalogContext CreateContextCore(DbCommandInterceptor? extra = null)
	{
		var builder = new DbContextOptionsBuilder<CatalogContext>()
			.UseNpgsql(ConnectionString)
			.UseBuildingBlocksPagination();
		if (extra is not null)
		{
			builder.AddInterceptors(extra);
		}

		return new CatalogContext(builder.Options);
	}

	private static bool DockerAvailable()
	{
		try
		{
			EnsureDockerOnPath();
			var psi = new ProcessStartInfo
			{
				FileName = "docker",
				Arguments = "info",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using var process = Process.Start(psi);
			if (process is null)
			{
				return false;
			}

			if (!process.WaitForExit(8000))
			{
				try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
				return false;
			}

			return process.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static void EnsureDockerOnPath()
	{
		const string dockerBin = @"C:\Program Files\Docker\Docker\resources\bin";
		if (OperatingSystem.IsWindows() && Directory.Exists(dockerBin))
		{
			var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
			if (!path.Contains(dockerBin, StringComparison.OrdinalIgnoreCase))
			{
				Environment.SetEnvironmentVariable("PATH", dockerBin + Path.PathSeparator + path);
			}
		}
	}
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PostgresFactAttribute : FactAttribute
{
	public PostgresFactAttribute()
	{
		if (PostgresFixture.IsRequiredEnvironment)
		{
			return;
		}

		if (!PostgresFixture.CanRun)
		{
			Skip = "PostgreSQL EF tests require Docker (Testcontainers).";
		}
	}
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class PostgresCiGuardTests
{
	[Fact]
	public void Ci_Requires_Postgres_Tuple_Tests_To_Execute()
	{
		if (!PostgresFixture.IsRequiredEnvironment)
		{
			return;
		}

		Assert.True(
			PostgresFixture.CanRun,
			"PostgreSQL EF CI requires Docker so Testcontainers can start postgres:16-alpine. Skipped tests are a CI failure.");
	}
}

[Collection("postgres")]
public sealed class PostgresTupleEfTests(PostgresFixture fixture)
{
	[PostgresFact]
	public void Postgres_Tuple_Seek_Query_Uses_Row_Comparison()
	{
		using var db = fixture.CreateContext();
		var first = CatalogSeed.Items.OrderBy(i => i.Price).ThenBy(i => i.Id).First();
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			CatalogSeed.ByPrice,
			[first.Price, first.Id],
			walkBackward: false);
		Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
		Assert.Contains(">", sql, StringComparison.Ordinal);
		Assert.True(
			sql.Contains("Price", StringComparison.OrdinalIgnoreCase)
			&& sql.Contains("Id", StringComparison.OrdinalIgnoreCase));
		Assert.True(
			sql.Contains(">", StringComparison.Ordinal) || sql.Contains("GreaterThan", StringComparison.Ordinal));
	}

	[PostgresFact]
	public void Postgres_Tuple_Four_Column_Seek_Uses_Row_Comparison()
	{
		using var db = fixture.CreateContext();
		var key = SortKey.For<CatalogItem>()
			.By(x => x.Price)
			.ThenBy(x => x.CreatedAt)
			.ThenBy(x => x.LongId)
			.ThenByUnique(x => x.Id);
		var first = CatalogSeed.Items.OrderBy(i => i.Price).ThenBy(i => i.CreatedAt).ThenBy(i => i.LongId).ThenBy(i => i.Id).First();
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			key,
			[first.Price, first.CreatedAt, first.LongId, first.Id],
			walkBackward: false);
		Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
		Assert.True(
			sql.Contains(">", StringComparison.Ordinal) || sql.Contains("GreaterThan", StringComparison.Ordinal),
			"Expected Postgres row comparison (SQL > or LINQ GreaterThan). SQL: " + sql);
	}

	[PostgresFact]
	public void Postgres_Tuple_Three_Column_Seek_Uses_Row_Comparison()
	{
		using var db = fixture.CreateContext();
		var first = CatalogSeed.Items.OrderBy(i => i.Price).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id).First();
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			CatalogSeed.ByPriceCreatedAt,
			[first.Price, first.CreatedAt, first.Id],
			walkBackward: false);
		Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
		Assert.True(
			sql.Contains(">", StringComparison.Ordinal) || sql.Contains("GreaterThan", StringComparison.Ordinal),
			"Expected Postgres row comparison for Price+CreatedAt+Id. SQL: " + sql);
		Assert.True(
			sql.Contains("Price", StringComparison.OrdinalIgnoreCase)
			&& sql.Contains("CreatedAt", StringComparison.OrdinalIgnoreCase)
			&& sql.Contains("Id", StringComparison.OrdinalIgnoreCase));
	}

	[PostgresFact]
	public void Postgres_Tuple_Nine_Column_Seek_Uses_Row_Comparison()
	{
		using var db = fixture.CreateContext();
		var first = CatalogSeed.Items
			.OrderBy(i => i.Price).ThenBy(i => i.CreatedAt).ThenBy(i => i.LongId).ThenBy(i => i.Kind)
			.ThenBy(i => i.ExternalId).ThenBy(i => i.VendorId).ThenBy(i => i.Flag).ThenBy(i => i.Rank).ThenBy(i => i.Id)
			.First();
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			CatalogSeed.ByNineValueTypes,
			[first.Price, first.CreatedAt, first.LongId, first.Kind, first.ExternalId, first.VendorId, first.Flag, first.Rank, first.Id],
			walkBackward: false);
		Assert.DoesNotContain(" OR ", sql, StringComparison.Ordinal);
	}

	[PostgresFact]
	public void Postgres_Tuple_Skipped_When_Mixed_Direction()
	{
		using var db = fixture.CreateContext();
		var key = SortKey.For<CatalogItem>().ByDescending(x => x.Price).ThenByUnique(x => x.Id);
		var first = CatalogSeed.Items.OrderByDescending(i => i.Price).ThenBy(i => i.Id).First();
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			key,
			[first.Price, first.Id],
			walkBackward: false);
		Assert.Contains(" OR ", sql, StringComparison.Ordinal);
	}

	[PostgresFact]
	public void Postgres_Tuple_Skipped_When_String_Slot()
	{
		using var db = fixture.CreateContext();
		var first = CatalogSeed.Items.OrderBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Id).First();
		var sql = EntityFrameworkCursorExtensions.DebugSeekQueryString(
			db.Items.AsQueryable(),
			CatalogSeed.ByName,
			[first.Name, first.Id],
			walkBackward: false);
		Assert.Contains(" OR ", sql, StringComparison.Ordinal);
	}

	[PostgresFact]
	public async Task Postgres_Two_Column_Execute_Next_Page()
	{
		await using var db = fixture.CreateContext();
		var expected = CatalogSeed.Items.OrderBy(i => i.Price).ThenBy(i => i.Id).Select(i => i.Id).ToList();
		var first = await db.Items.ToCursorPageAsync(new CursorRequest(null, 4), CatalogSeed.ByPrice);
		Assert.Equal(expected.Take(4), first.Items.Select(i => i.Id));
		var second = await db.Items.ToCursorPageAsync(new CursorRequest(first.Next, 4), CatalogSeed.ByPrice);
		Assert.Equal(expected.Skip(4).Take(4), second.Items.Select(i => i.Id));
	}

	[PostgresFact]
	public async Task Postgres_OrderBy_Nulls_Last_When_Interceptor_Registered()
	{
		var capture = new CaptureCommands();
		await using var db = fixture.CreateContext(capture);
		var key = SortKey.For<CatalogItem>().By(x => x.Name).ThenByUnique(x => x.Id);
		await db.Items.ToCursorPageAsync(
			new CursorRequest(null, 5), key, new PaginationOptions { Nulls = NullOrder.Last });
		Assert.Contains(capture.Commands, c => c.Contains("NULLS LAST", StringComparison.OrdinalIgnoreCase));
	}

	private sealed class CaptureCommands : DbCommandInterceptor
	{
		public List<string> Commands { get; } = [];

		public override InterceptionResult<DbDataReader> ReaderExecuting(
			DbCommand command,
			CommandEventData eventData,
			InterceptionResult<DbDataReader> result)
		{
			Commands.Add(command.CommandText);
			return base.ReaderExecuting(command, eventData, result);
		}

		public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
			DbCommand command,
			CommandEventData eventData,
			InterceptionResult<DbDataReader> result,
			CancellationToken cancellationToken = default)
		{
			Commands.Add(command.CommandText);
			return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
		}
	}
}
