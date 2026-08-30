using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.SqlServer.Tests;

[CollectionDefinition(Name)]
public sealed class SqlServerHintCollection : ICollectionFixture<SqlServerHintFixture>
{
	public const string Name = "SqlServerHint";
}

/// <summary>
/// Slim Aspire SQL Server (or <c>PAGINATION_SQLSERVER</c>). Not the lab AppHost / AspireFixture.
/// </summary>
public sealed class SqlServerHintFixture : IAsyncLifetime
{
	private DistributedApplication? _app;
	private IResourceBuilder<SqlServerDatabaseResource>? _database;

	public string ConnectionString { get; private set; } = "";

	/// <summary>GitHub Actions or an explicit required flag. Skips become failures.</summary>
	public static bool IsRequiredEnvironment
	{
		get
		{
			if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			var required = Environment.GetEnvironmentVariable("PAGINATION_SQLSERVER_REQUIRED");
			return string.Equals(required, "true", StringComparison.OrdinalIgnoreCase)
				|| required == "1";
		}
	}

	public static bool CanRun
	{
		get
		{
			if (IsRequiredEnvironment)
			{
				return DockerAvailable();
			}

			if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAGINATION_SQLSERVER")))
			{
				return true;
			}

			return DockerAvailable();
		}
	}

	public string PooledConnectionString
	{
		get
		{
			var b = new SqlConnectionStringBuilder(ConnectionString) { Pooling = true };
			return b.ConnectionString;
		}
	}

	public async Task InitializeAsync()
	{
		if (IsRequiredEnvironment)
		{
			if (!DockerAvailable())
			{
				throw new InvalidOperationException(
					"SQL Server QueryHint CI requires Docker so Aspire can start mcr.microsoft.com/mssql/server:2022-latest.");
			}

			await StartAspireAsync();
			return;
		}

		var env = Environment.GetEnvironmentVariable("PAGINATION_SQLSERVER");
		if (!string.IsNullOrWhiteSpace(env))
		{
			ConnectionString = Harden(env, pooling: false);
			await WaitForSqlAsync(ConnectionString);
			await EnsureDatabaseAsync(ConnectionString);
			await EnsureSeedAsync();
			return;
		}

		EnsureDockerContainerRuntime();

		await StartAspireAsync();
	}

	public HintContext CreateContext(params IInterceptor[] interceptors)
		=> CreateContext(pooling: false, interceptors);

	public HintContext CreateContext(bool pooling, params IInterceptor[] interceptors)
	{
		var builder = new DbContextOptionsBuilder<HintContext>().UseSqlServer(WithPooling(ConnectionString, pooling));
		if (interceptors.Length > 0)
		{
			builder.AddInterceptors(interceptors);
		}

		return new HintContext(builder.Options);
	}

	public async Task DisposeAsync()
	{
		if (_app is null)
		{
			return;
		}

		await _app.StopAsync();
		if (_app is IAsyncDisposable asyncDisposable)
		{
			await asyncDisposable.DisposeAsync();
		}
	}

	private async Task EnsureSeedAsync()
	{
		await using var db = CreateContext();
		await db.Database.EnsureCreatedAsync();
		if (!await db.Items.AnyAsync(r => r.Id == 1))
		{
			db.Items.AddRange(
				new HintRow { Id = 1, Name = "alpha" },
				new HintRow { Id = 2, Name = "beta" },
				new HintRow { Id = 3, Name = "gamma" });
			await db.SaveChangesAsync();
		}
	}

	private static async Task EnsureDatabaseAsync(string connectionString)
	{
		var target = new SqlConnectionStringBuilder(connectionString);
		var dbName = target.InitialCatalog;
		if (string.IsNullOrWhiteSpace(dbName) || string.Equals(dbName, "master", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
		await using var conn = new SqlConnection(master.ConnectionString);
		await conn.OpenAsync();
		await using var cmd = conn.CreateCommand();
		cmd.CommandText = """
			IF DB_ID(@name) IS NULL
			BEGIN
				DECLARE @sql nvarchar(512) = N'CREATE DATABASE ' + QUOTENAME(@name);
				EXEC (@sql);
			END
			""";
		var p = cmd.CreateParameter();
		p.ParameterName = "@name";
		p.Value = dbName;
		cmd.Parameters.Add(p);
		await cmd.ExecuteNonQueryAsync();

		await using var rcs = conn.CreateCommand();
		rcs.CommandText = """
			IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @name AND is_read_committed_snapshot_on = 0)
			BEGIN
				DECLARE @alter nvarchar(512) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE';
				EXEC (@alter);
			END
			""";
		var p2 = rcs.CreateParameter();
		p2.ParameterName = "@name";
		p2.Value = dbName;
		rcs.Parameters.Add(p2);
		await rcs.ExecuteNonQueryAsync();
	}

	private async Task StartAspireAsync()
	{
		EnsureDockerContainerRuntime();

		var options = new DistributedApplicationOptions
		{
			AssemblyName = typeof(SqlServerHintFixture).Assembly.FullName,
			DisableDashboard = true
		};
		var builder = DistributedApplication.CreateBuilder(options);
		var password = builder.AddParameter("sql-password", "Pagination_Hint_1", secret: true);
		var sql = builder.AddSqlServer("sql", password: password);
		_database = sql.AddDatabase("pagination-hint");
		_app = builder.Build();
		await _app.StartAsync();

		using var waitCts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
		await _app.ResourceNotifications.WaitForResourceAsync("sql", KnownResourceStates.Running, waitCts.Token);

		ConnectionString = Harden(
			await ((IResourceWithConnectionString)_database.Resource).GetConnectionStringAsync()
			?? throw new InvalidOperationException("SQL Server connection string was not available."),
			pooling: false);

		await WaitForSqlAsync(ConnectionString);
		await EnsureDatabaseAsync(ConnectionString);
		await EnsureSeedAsync();
	}

	private static string WithPooling(string connectionString, bool pooling)
	{
		var b = new SqlConnectionStringBuilder(connectionString) { Pooling = pooling };
		return b.ConnectionString;
	}

	private static string Harden(string connectionString, bool pooling)
	{
		var b = new SqlConnectionStringBuilder(connectionString)
		{
			TrustServerCertificate = true,
			Encrypt = SqlConnectionEncryptOption.Optional,
			ConnectTimeout = 15,
			Pooling = pooling
		};
		return b.ConnectionString;
	}

	private static async Task WaitForSqlAsync(string connectionString)
	{
		var master = new SqlConnectionStringBuilder(connectionString)
		{
			InitialCatalog = "master"
		};
		for (var i = 0; i < 60; i++)
		{
			try
			{
				await using var conn = new SqlConnection(master.ConnectionString);
				await conn.OpenAsync();
				return;
			}
			catch when (i < 59)
			{
				await Task.Delay(2000);
			}
		}

		throw new InvalidOperationException("SQL Server did not become ready.");
	}

	private static void EnsureDockerContainerRuntime()
	{
		Environment.SetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME", "docker");
		Environment.SetEnvironmentVariable("DOTNET_ASPIRE_CONTAINER_RUNTIME", "docker");

		const string dockerBin = @"C:\Program Files\Docker\Docker\resources\bin";
		if (OperatingSystem.IsWindows() && Directory.Exists(dockerBin))
		{
			var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
			if (!path.Contains(dockerBin, StringComparison.OrdinalIgnoreCase))
			{
				Environment.SetEnvironmentVariable("PATH", dockerBin + Path.PathSeparator + path);
			}
		}

		if (OperatingSystem.IsWindows()
			&& string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
		{
			Environment.SetEnvironmentVariable("DOCKER_HOST", "npipe:////./pipe/dockerDesktopLinuxEngine");
		}
	}

	private static bool DockerAvailable()
	{
		try
		{
			EnsureDockerContainerRuntime();
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
}

public sealed class HintRow
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
}

public sealed class HintContext : DbContext
{
	public HintContext(DbContextOptions<HintContext> options) : base(options) { }

	public DbSet<HintRow> Items => Set<HintRow>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<HintRow>().ToTable("hint_rows");
		modelBuilder.Entity<HintRow>().HasKey(r => r.Id);
		modelBuilder.Entity<HintRow>().Property(r => r.Id).ValueGeneratedNever();
		modelBuilder.Entity<HintRow>().Property(r => r.Name).HasMaxLength(64);
	}
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SqlServerFactAttribute : FactAttribute
{
	public SqlServerFactAttribute()
	{
		if (SqlServerHintFixture.IsRequiredEnvironment)
		{
			return;
		}

		if (!SqlServerHintFixture.CanRun)
		{
			Skip = "SQL Server QueryHint tests require Docker or PAGINATION_SQLSERVER.";
		}
	}
}
