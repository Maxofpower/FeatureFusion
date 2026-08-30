namespace BuildingBlocks.Pagination.Dapper;

/// <summary>SQL dialect for generated seek / ORDER BY / limit clauses.</summary>
public enum SqlDialect
{
	/// <summary>PostgreSQL: row comparison when directions match; LIMIT.</summary>
	PostgreSql = 0,

	/// <summary>SQL Server: OR-seek + OFFSET 0 ROWS FETCH NEXT n ROWS ONLY.</summary>
	SqlServer = 1,

	/// <summary>Sqlite: OR-seek + LIMIT.</summary>
	Sqlite = 2
}
