namespace BuildingBlocks.Pagination.Dapper;

internal static class QueryHintSql
{
	internal const string SqlServerReadUncommitted = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; ";
	internal const string SqlServerReadCommitted = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;";

	public static string Apply(QueryHint hint, SqlDialect dialect, string sql)
	{
		if (IsSqlServerReadUncommitted(hint, dialect))
		{
			return SqlServerReadUncommitted + sql;
		}

		return sql;
	}

	internal static bool IsSqlServerReadUncommitted(QueryHint hint, SqlDialect dialect)
		=> hint == QueryHint.ReadUncommitted && dialect == SqlDialect.SqlServer;
}
