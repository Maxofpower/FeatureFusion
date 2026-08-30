using System.Data;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Infrastructure.Internal;

internal static class QueryHintExecutor
{
	private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
	private const string RestoreReadCommittedSql = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;";

	public static ValueTask<TResult> RunAsync<TResult>(
		IQueryable query,
		PaginationOptions options,
		Func<CancellationToken, Task<TResult>> action,
		CancellationToken cancellationToken)
	{
		if (options.Hint == QueryHint.None || !CanApplyReadUncommitted(query, options))
		{
			return new ValueTask<TResult>(action(cancellationToken));
		}

		return new ValueTask<TResult>(RunWithTransactionAsync(query, action, cancellationToken));
	}

	private static bool CanApplyReadUncommitted(IQueryable query, PaginationOptions options)
	{
		if (options.Hint != QueryHint.ReadUncommitted)
		{
			return false;
		}

		var ctx = CursorDbContext.TryGet(query);
		return ctx is not null && IsSqlServer(ctx) && ctx.Database.CurrentTransaction is null;
	}

	private static async Task<TResult> RunWithTransactionAsync<TResult>(
		IQueryable query,
		Func<CancellationToken, Task<TResult>> action,
		CancellationToken cancellationToken)
	{
		var ctx = CursorDbContext.TryGet(query)!;
		TResult? result = default;
		Exception? error = null;
		try
		{
			await using (var tx = await ctx.Database
				.BeginTransactionAsync(IsolationLevel.ReadUncommitted, cancellationToken)
				.ConfigureAwait(false))
			{
				try
				{
					result = await action(cancellationToken).ConfigureAwait(false);
					await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					error = ex;
					try
					{
						await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
					}
					catch
					{
						// Keep the pagination exception/cancellation.
					}
				}
			}
		}
		catch (Exception ex)
		{
			error ??= ex;
		}

		try
		{
			await RestoreReadCommittedIfOpenAsync(ctx).ConfigureAwait(false);
		}
		catch (Exception restoreEx)
		{
			error ??= restoreEx;
		}

		if (error is not null)
		{
			ExceptionDispatchInfo.Capture(error).Throw();
		}

		return result!;
	}

	/// <summary>
	/// SqlClient keeps <c>SET TRANSACTION ISOLATION LEVEL</c> after commit/rollback on an
	/// still-open connection. Pool reset is not enough when the host already opened it.
	/// </summary>
	private static async Task RestoreReadCommittedIfOpenAsync(DbContext ctx)
	{
		var connection = ctx.Database.GetDbConnection();
		if (connection.State != ConnectionState.Open)
		{
			return;
		}

		await using var command = connection.CreateCommand();
		command.Transaction = null;
		command.CommandText = RestoreReadCommittedSql;
		await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
	}

	private static bool IsSqlServer(DbContext ctx)
		=> string.Equals(ctx.Database.ProviderName, SqlServerProvider, StringComparison.Ordinal);
}
