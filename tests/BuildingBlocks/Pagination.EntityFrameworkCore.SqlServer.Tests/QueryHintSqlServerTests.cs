using System.Data;
using System.Data.Common;
using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.SqlServer.Tests;

[Collection(SqlServerHintCollection.Name)]
public sealed class QueryHintSqlServerTests
{
	private static readonly SortKey<HintRow> ById = SortKey.For<HintRow>().ThenByUnique(x => x.Id);
	private static int _nextDirtyId = 10_000;

	private readonly SqlServerHintFixture _fixture;

	public QueryHintSqlServerTests(SqlServerHintFixture fixture) => _fixture = fixture;

	[SqlServerFact]
	public async Task QueryHint_None_DefaultIsolation_Paginates()
	{
		var dirtyId = NextDirtyId();
		await using var writer = _fixture.CreateContext();
		await writer.Database.OpenConnectionAsync();
		await using var dirty = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
		writer.Items.Add(new HintRow { Id = dirtyId, Name = "dirty" });
		await writer.SaveChangesAsync();

		await using var reader = _fixture.CreateContext();
		var page = await reader.Items.ToCursorPageAsync(
			new CursorRequest(null, 20),
			ById,
			new PaginationOptions { Hint = QueryHint.None });

		Assert.Contains(1, page.Items.Select(r => r.Id));
		Assert.DoesNotContain(dirtyId, page.Items.Select(r => r.Id));
		Assert.Null(reader.Database.CurrentTransaction);
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_DirtyRead()
	{
		var dirtyId = NextDirtyId();
		await using var writer = _fixture.CreateContext();
		await writer.Database.OpenConnectionAsync();
		await using var dirty = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
		writer.Items.Add(new HintRow { Id = dirtyId, Name = "dirty" });
		await writer.SaveChangesAsync();

		await using var reader = _fixture.CreateContext();
		var page = await reader.Items.ToCursorPageAsync(
			new CursorRequest(null, 20),
			ById,
			new PaginationOptions { Hint = QueryHint.ReadUncommitted });

		Assert.Contains(dirtyId, page.Items.Select(r => r.Id));
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_CountAndPage_SameIsolation()
	{
		var dirtyId = NextDirtyId();
		await using var writer = _fixture.CreateContext();
		await writer.Database.OpenConnectionAsync();
		await using var dirty = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
		writer.Items.Add(new HintRow { Id = dirtyId, Name = "dirty-count" });
		await writer.SaveChangesAsync();

		await using var reader = _fixture.CreateContext();
		var committed = await reader.Items.CountAsync();
		var page = await reader.Items.ToCursorPageAsync(
			new CursorRequest(null, 50),
			ById,
			new PaginationOptions { Hint = QueryHint.ReadUncommitted, IncludeTotalCount = true });

		Assert.Equal(committed + 1, page.TotalCount);
		Assert.Contains(dirtyId, page.Items.Select(r => r.Id));
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_Commits()
	{
		await using var db = _fixture.CreateContext();
		var page = await db.Items.ToCursorPageAsync(
			new CursorRequest(null, 5),
			ById,
			new PaginationOptions { Hint = QueryHint.ReadUncommitted });

		Assert.NotEmpty(page.Items);
		Assert.Null(db.Database.CurrentTransaction);
		var again = await db.Items.ToCursorPageAsync(new CursorRequest(null, 5), ById);
		Assert.NotEmpty(again.Items);
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_Exception_RollsBack()
	{
		await using var db = _fixture.CreateContext(new ThrowAfterStartInterceptor());
		await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await db.Items.ToCursorPageAsync(
				new CursorRequest(null, 5),
				ById,
				new PaginationOptions { Hint = QueryHint.ReadUncommitted }));

		Assert.Null(db.Database.CurrentTransaction);
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_Cancellation_CleansUp()
	{
		using var cts = new CancellationTokenSource();
		await using var db = _fixture.CreateContext(new CancelOnReaderInterceptor(cts));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			async () => await db.Items.ToCursorPageAsync(
				new CursorRequest(null, 5),
				ById,
				new PaginationOptions { Hint = QueryHint.ReadUncommitted },
				cts.Token));

		Assert.Null(db.Database.CurrentTransaction);
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_AmbientTransaction_IsIgnored()
	{
		var dirtyId = NextDirtyId();
		await using var writer = _fixture.CreateContext();
		await writer.Database.OpenConnectionAsync();
		await using var dirty = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
		writer.Items.Add(new HintRow { Id = dirtyId, Name = "ambient" });
		await writer.SaveChangesAsync();

		await using var reader = _fixture.CreateContext();
		await reader.Database.OpenConnectionAsync();
		await using var ambient = await reader.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
		var page = await reader.Items.ToCursorPageAsync(
			new CursorRequest(null, 20),
			ById,
			new PaginationOptions { Hint = QueryHint.ReadUncommitted });

		Assert.Same(ambient, reader.Database.CurrentTransaction);
		Assert.Equal(2, await IsolationLevelAsync(reader));
		Assert.DoesNotContain(dirtyId, page.Items.Select(r => r.Id));
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_Restores_ReadCommitted_On_Open_Pooled_Connection()
	{
		var dirtyId = NextDirtyId();
		await using var writer = _fixture.CreateContext();
		await writer.Database.OpenConnectionAsync();
		await using var dirty = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
		writer.Items.Add(new HintRow { Id = dirtyId, Name = "iso-dirty" });
		await writer.SaveChangesAsync();

		var capture = new IsolationCaptureInterceptor();
		await using var reader = _fixture.CreateContext(pooling: true, capture);
		await reader.Database.OpenConnectionAsync();
		Assert.Equal(2, await IsolationLevelAsync(reader));

		var page = await reader.Items.ToCursorPageAsync(
			new CursorRequest(null, 50),
			ById,
			new PaginationOptions { Hint = QueryHint.ReadUncommitted, IncludeTotalCount = true });

		Assert.Contains(dirtyId, page.Items.Select(r => r.Id));
		Assert.Equal(1, capture.Isolation);
		Assert.Equal(ConnectionState.Open, reader.Database.GetDbConnection().State);
		Assert.Null(reader.Database.CurrentTransaction);
		Assert.Equal(2, await IsolationLevelAsync(reader));

		var committed = await reader.Items.ToCursorPageAsync(
			new CursorRequest(null, 50),
			ById,
			new PaginationOptions { Hint = QueryHint.None });
		Assert.DoesNotContain(dirtyId, committed.Items.Select(r => r.Id));
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_Restores_ReadCommitted_After_Exception()
	{
		var capture = new IsolationCaptureInterceptor();
		await using var db = _fixture.CreateContext(pooling: true, capture, new ThrowAfterStartInterceptor());
		await db.Database.OpenConnectionAsync();
		Assert.Equal(2, await IsolationLevelAsync(db));

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await db.Items.ToCursorPageAsync(
				new CursorRequest(null, 5),
				ById,
				new PaginationOptions { Hint = QueryHint.ReadUncommitted }));

		Assert.Equal("hint-test-boom", ex.Message);
		Assert.Equal(1, capture.Isolation);
		Assert.Null(db.Database.CurrentTransaction);
		Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
		Assert.Equal(2, await IsolationLevelAsync(db));
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_Restores_ReadCommitted_After_Cancellation()
	{
		using var cts = new CancellationTokenSource();
		var capture = new IsolationCaptureInterceptor();
		await using var db = _fixture.CreateContext(pooling: true, capture, new CancelOnReaderInterceptor(cts));
		await db.Database.OpenConnectionAsync();
		Assert.Equal(2, await IsolationLevelAsync(db));

		var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
			async () => await db.Items.ToCursorPageAsync(
				new CursorRequest(null, 5),
				ById,
				new PaginationOptions { Hint = QueryHint.ReadUncommitted },
				cts.Token));

		Assert.True(ex is OperationCanceledException);
		Assert.Equal(1, capture.Isolation);
		Assert.Null(db.Database.CurrentTransaction);
		Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
		Assert.Equal(2, await IsolationLevelAsync(db));
	}

	private static int NextDirtyId() => Interlocked.Increment(ref _nextDirtyId);

	private static async Task<int> IsolationLevelAsync(HintContext db)
	{
		var connection = db.Database.GetDbConnection();
		if (connection.State != ConnectionState.Open)
		{
			await db.Database.OpenConnectionAsync();
		}

		await using var cmd = connection.CreateCommand();
		cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
		cmd.CommandText = "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID";
		return Convert.ToInt32(await cmd.ExecuteScalarAsync());
	}
}

file sealed class IsolationCaptureInterceptor : DbCommandInterceptor
{
	public int? Isolation { get; private set; }

	public override InterceptionResult<DbDataReader> ReaderExecuting(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result)
	{
		Capture(command);
		return result;
	}

	public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result,
		CancellationToken cancellationToken = default)
	{
		Capture(command);
		return new ValueTask<InterceptionResult<DbDataReader>>(result);
	}

	private void Capture(DbCommand command)
	{
		if (command.Connection is null
			|| command.CommandText.Contains("transaction_isolation_level", StringComparison.OrdinalIgnoreCase)
			|| command.CommandText.StartsWith("SET TRANSACTION ISOLATION LEVEL", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		using var probe = command.Connection.CreateCommand();
		probe.Transaction = command.Transaction;
		probe.CommandText = "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID";
		Isolation = Convert.ToInt32(probe.ExecuteScalar());
	}
}

file sealed class ThrowAfterStartInterceptor : DbCommandInterceptor
{
	public override InterceptionResult<DbDataReader> ReaderExecuting(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result)
		=> throw new InvalidOperationException("hint-test-boom");

	public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result,
		CancellationToken cancellationToken = default)
		=> throw new InvalidOperationException("hint-test-boom");
}

file sealed class CancelOnReaderInterceptor : DbCommandInterceptor
{
	private readonly CancellationTokenSource _cts;

	public CancelOnReaderInterceptor(CancellationTokenSource cts) => _cts = cts;

	public override InterceptionResult<DbDataReader> ReaderExecuting(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result)
	{
		_cts.Cancel();
		return base.ReaderExecuting(command, eventData, result);
	}

	public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result,
		CancellationToken cancellationToken = default)
	{
		_cts.Cancel();
		return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
	}
}
