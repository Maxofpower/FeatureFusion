using System.Data;
using BuildingBlocks.Pagination;
using BuildingBlocks.Pagination.Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.SqlServer.Tests;

[Collection(SqlServerHintCollection.Name)]
public sealed class QueryHintDapperSqlServerTests
{
	private static readonly SortKey<HintRow> ById =
		SortKey.For<HintRow>().ThenByUnique(x => x.Id, sql: "Id");

	private const string FilterSql = "SELECT Id, Name FROM hint_rows";
	private static int _nextDirtyId = 20_000;

	private readonly SqlServerHintFixture _fixture;

	public QueryHintDapperSqlServerTests(SqlServerHintFixture fixture) => _fixture = fixture;

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_Restores_ReadCommitted_On_Open_Connection()
	{
		var dirtyId = Interlocked.Increment(ref _nextDirtyId);
		await using var writer = _fixture.CreateContext();
		await writer.Database.OpenConnectionAsync();
		await using var dirty = await writer.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
		writer.Items.Add(new HintRow { Id = dirtyId, Name = "dapper-dirty" });
		await writer.SaveChangesAsync();

		await using var conn = new SqlConnection(_fixture.PooledConnectionString);
		await conn.OpenAsync();
		Assert.Equal(2, await IsolationAsync(conn));

		var page = await conn.QueryCursorPageAsync<HintRow>(
			new CursorRequest(null, 50),
			ById,
			FilterSql,
			SqlDialect.SqlServer,
			options: new PaginationOptions { Hint = QueryHint.ReadUncommitted, IncludeTotalCount = true });

		Assert.Contains(dirtyId, page.Items.Select(r => r.Id));
		Assert.Equal(ConnectionState.Open, conn.State);
		Assert.Equal(2, await IsolationAsync(conn));

		var committed = await conn.QueryCursorPageAsync<HintRow>(
			new CursorRequest(null, 50),
			ById,
			FilterSql,
			SqlDialect.SqlServer,
			options: new PaginationOptions { Hint = QueryHint.None });
		Assert.DoesNotContain(dirtyId, committed.Items.Select(r => r.Id));
	}

	[SqlServerFact]
	public async Task QueryHint_ReadUncommitted_Restores_ReadCommitted_After_Exception()
	{
		await using var conn = new SqlConnection(_fixture.PooledConnectionString);
		await conn.OpenAsync();
		Assert.Equal(2, await IsolationAsync(conn));

		var ex = await Assert.ThrowsAnyAsync<Exception>(
			async () => await conn.QueryCursorPageAsync<HintRow>(
				new CursorRequest(null, 5),
				ById,
				"SELECT Id, Name, MissingColumn FROM hint_rows",
				SqlDialect.SqlServer,
				options: new PaginationOptions { Hint = QueryHint.ReadUncommitted }));

		Assert.False(ex is OperationCanceledException);
		Assert.Equal(ConnectionState.Open, conn.State);
		Assert.Equal(2, await IsolationAsync(conn));
	}

	private static async Task<int> IsolationAsync(SqlConnection conn)
	{
		await using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id = @@SPID";
		return Convert.ToInt32(await cmd.ExecuteScalarAsync());
	}
}
