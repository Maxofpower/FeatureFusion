using System.Data;
using System.Runtime.ExceptionServices;
using System.Text;
using Dapper;

namespace BuildingBlocks.Pagination.Dapper;

/// <summary>Keyset pagination over Dapper / <see cref="IDbConnection"/>.</summary>
public static class DapperCursorExtensions
{
	/// <summary>
	/// Pages a host SELECT (no ORDER BY / OFFSET). The library wraps it as a subquery and appends seek, ORDER BY, and limit.
	/// </summary>
	/// <typeparam name="T">Row type mapped by Dapper.</typeparam>
	/// <param name="connection">Open or closed connection.</param>
	/// <param name="request">Cursor and limit.</param>
	/// <param name="sortKey">Sort key with <c>sql:</c> on every slot.</param>
	/// <param name="sql">Filter query (SELECT ... FROM ... WHERE ...).</param>
	/// <param name="dialect">Engine dialect.</param>
	/// <param name="param">Host parameters.</param>
	/// <param name="options">Optional options. <see cref="PaginationOptions.Hint"/> defaults to <see cref="QueryHint.None"/>.</param>
	/// <param name="cancellationToken">Cancellation (passed to Dapper command).</param>
	public static async ValueTask<CursorPage<T>> QueryCursorPageAsync<T>(
		this IDbConnection connection,
		CursorRequest request,
		SortKey<T> sortKey,
		string sql,
		SqlDialect dialect,
		object? param = null,
		PaginationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		options ??= PaginationOptions.Default;
		HostSql.EnsureFilterOnly(sql);
		sortKey.EnsureNoShadow();
		sortKey.EnsureSqlIdentifiers();
		var (walkBackward, values, fromCursor) = RequestCursor.Resolve(request, sortKey, options);
		var restoreIsolation = QueryHintSql.IsSqlServerReadUncommitted(options.Hint, dialect);

		CursorPage<T>? page = null;
		Exception? error = null;
		try
		{
			int? total = null;
			if (options.IncludeTotalCount)
			{
				var countSql = QueryHintSql.Apply(
					options.Hint,
					dialect,
					$"SELECT COUNT(*) FROM ({sql}) AS _bbp_count");
				total = await connection.ExecuteScalarAsync<int>(
						new CommandDefinition(countSql, param, cancellationToken: cancellationToken))
					.ConfigureAwait(false);
			}

			var command = SeekSql.Build(sql, sortKey, values, walkBackward, request.Limit + 1, dialect, param);
			var pageSql = QueryHintSql.Apply(options.Hint, dialect, command.Sql);
			var fetched = (await connection.QueryAsync<T>(
					new CommandDefinition(pageSql, command.Parameters, cancellationToken: cancellationToken))
				.ConfigureAwait(false)).AsList();
			var keys = new List<object?[]>(fetched.Count);
			for (var i = 0; i < fetched.Count; i++)
			{
				keys.Add(KeyExtractor.GetValues(fetched[i], sortKey));
			}

			page = PageAssembler.Assemble(fetched, keys, sortKey, request.Limit, walkBackward, fromCursor, options, total);
		}
		catch (Exception ex)
		{
			error = ex;
		}
		finally
		{
			if (restoreIsolation)
			{
				try
				{
					await RestoreReadCommittedIfOpenAsync(connection).ConfigureAwait(false);
				}
				catch (Exception restoreEx)
				{
					error ??= restoreEx;
				}
			}
		}

		if (error is not null)
		{
			ExceptionDispatchInfo.Capture(error).Throw();
		}

		return page!;
	}

	/// <summary>
	/// <c>SET TRANSACTION ISOLATION LEVEL</c> is session-scoped. Restore only when the host
	/// connection is still open; a closed connection is reset on the next pool checkout.
	/// </summary>
	private static async Task RestoreReadCommittedIfOpenAsync(IDbConnection connection)
	{
		if (connection.State != ConnectionState.Open)
		{
			return;
		}

		await connection.ExecuteAsync(
				new CommandDefinition(QueryHintSql.SqlServerReadCommitted, cancellationToken: CancellationToken.None))
			.ConfigureAwait(false);
	}

	/// <summary>Builds the SQL that <see cref="QueryCursorPageAsync{T}"/> would execute (for tests).</summary>
	/// <typeparam name="T">Row type.</typeparam>
	/// <param name="sortKey">Sort key.</param>
	/// <param name="sql">Host SQL.</param>
	/// <param name="dialect">Dialect.</param>
	/// <param name="values">Seek values; null for first page.</param>
	/// <param name="walkBackward">Walk direction.</param>
	/// <param name="take">LIMIT/FETCH count (typically limit+1).</param>
	/// <param name="options">Optional; <see cref="PaginationOptions.Hint"/> prefixes SQL Server dirty-read SET.</param>
	public static string BuildSql<T>(
		SortKey<T> sortKey,
		string sql,
		SqlDialect dialect,
		object?[]? values,
		bool walkBackward,
		int take,
		PaginationOptions? options = null)
	{
		HostSql.EnsureFilterOnly(sql);
		sortKey.EnsureNoShadow();
		sortKey.EnsureSqlIdentifiers();
		var built = SeekSql.Build(sql, sortKey, values, walkBackward, take, dialect, param: null).Sql;
		return QueryHintSql.Apply(options?.Hint ?? QueryHint.None, dialect, built);
	}
}
