using System.Globalization;
using System.Text;
using Dapper;

namespace BuildingBlocks.Pagination.Dapper;

internal sealed record SeekCommand(string Sql, DynamicParameters Parameters);

internal static class SeekSql
{
	public static SeekCommand Build<T>(
		string hostSql,
		SortKey<T> sortKey,
		object?[]? values,
		bool walkBackward,
		int take,
		SqlDialect dialect,
		object? param)
	{
		var parameters = new DynamicParameters(param);
		parameters.Add("take", take);

		var sb = new StringBuilder();
		sb.Append("SELECT * FROM (").Append(hostSql).Append(") AS _bbp");

		if (values is not null)
		{
			sb.Append(" WHERE ");
			AppendSeek(sb, sortKey, values, walkBackward, dialect, parameters);
		}

		sb.Append(" ORDER BY ");
		AppendOrder(sb, sortKey, walkBackward, dialect);
		AppendLimit(sb, dialect);

		return new SeekCommand(sb.ToString(), parameters);
	}

	private static void AppendSeek<T>(
		StringBuilder sb,
		SortKey<T> sortKey,
		object?[] values,
		bool walkBackward,
		SqlDialect dialect,
		DynamicParameters parameters)
	{
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			parameters.Add("ks" + i.ToString(CultureInfo.InvariantCulture), values[i]);
		}

		if (dialect == SqlDialect.PostgreSql && SeekOps.TupleEligible(sortKey, walkBackward))
		{
			sb.Append('(');
			sb.Append(string.Join(", ", sortKey.Slots.Select(s => Quote(s.SqlIdentifier!, dialect))));
			sb.Append(')');
			sb.Append(SeekOps.UseGreater(sortKey.Slots[0], walkBackward) ? " > " : " < ");
			sb.Append('(');
			sb.Append(string.Join(", ", Enumerable.Range(0, sortKey.Slots.Count).Select(i => "@ks" + i)));
			sb.Append(')');
			return;
		}

		sb.Append('(');
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			if (i > 0)
			{
				sb.Append(" OR ");
			}

			sb.Append('(');
			for (var j = 0; j < i; j++)
			{
				sb.Append(Quote(sortKey.Slots[j].SqlIdentifier!, dialect))
					.Append(" = @ks").Append(j).Append(" AND ");
			}

			var op = SeekOps.UseGreater(sortKey.Slots[i], walkBackward) ? " > " : " < ";
			sb.Append(Quote(sortKey.Slots[i].SqlIdentifier!, dialect))
				.Append(op).Append("@ks").Append(i);
			sb.Append(')');
		}

		sb.Append(')');
	}

	private static void AppendOrder<T>(StringBuilder sb, SortKey<T> sortKey, bool walkBackward, SqlDialect dialect)
	{
		for (var i = 0; i < sortKey.Slots.Count; i++)
		{
			if (i > 0)
			{
				sb.Append(", ");
			}

			var dir = SeekOps.OrderDirection(sortKey.Slots[i], walkBackward);
			sb.Append(Quote(sortKey.Slots[i].SqlIdentifier!, dialect))
				.Append(dir == SortDirection.Ascending ? " ASC" : " DESC");
		}
	}

	private static void AppendLimit(StringBuilder sb, SqlDialect dialect)
	{
		switch (dialect)
		{
			case SqlDialect.SqlServer:
				sb.Append(" OFFSET 0 ROWS FETCH NEXT @take ROWS ONLY");
				break;
			default:
				sb.Append(" LIMIT @take");
				break;
		}
	}

	internal static string Quote(string identifier, SqlDialect dialect)
	{
		var parts = identifier.Split('.');
		return string.Join('.', parts.Select(p => dialect switch
		{
			SqlDialect.SqlServer => "[" + p + "]",
			_ => "\"" + p + "\""
		}));
	}
}
