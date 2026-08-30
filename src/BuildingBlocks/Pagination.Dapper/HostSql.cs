using System.Text.RegularExpressions;

namespace BuildingBlocks.Pagination.Dapper;

internal static partial class HostSql
{
	[GeneratedRegex(@"\b(ORDER\s+BY|OFFSET|FETCH\s+NEXT|LIMIT)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex PagingClause();

	public static void EnsureFilterOnly(string sql)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sql);
		if (PagingClause().IsMatch(sql))
		{
			throw new PaginationException(
				PaginationErrorCode.InvalidHostSql,
				"Host SQL must be a filter query (SELECT … FROM … WHERE …) with no ORDER BY, OFFSET, FETCH, or LIMIT. The adapter appends seek + ORDER BY + limit.");
		}
	}
}
