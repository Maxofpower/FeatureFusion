using BuildingBlocks.Pagination;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Infrastructure.Internal;

/// <summary>
/// Rewrites <c>ORDER BY</c> on tagged pagination commands to append <c>NULLS FIRST/LAST</c>
/// for Npgsql and Sqlite. Null placement is encoded in the query tag
/// (<c>BuildingBlocks.Pagination:First|Last</c>).
/// </summary>
/// <remarks>
/// Stock LINQ cannot emit SQL <c>NULLS FIRST/LAST</c>. Emitting <c>CASE</c> / <c>IS NULL</c>
/// in <c>OrderBy</c> would change null placement but typically prevents a matching btree
/// from being used. This interceptor only touches commands tagged by
/// <c>ToCursorPageAsync</c>, so host SQL is unchanged. <c>ToQueryString</c> does not run
/// interceptors — assert executed command text (or Dapper SQL) for NULLS, not the
/// LINQ string. SQL Server has no portable index-friendly NULLS syntax; we do not rewrite it.
/// </remarks>
internal sealed class PaginationNullsInterceptor : DbCommandInterceptor
{
	internal const string QueryTag = "BuildingBlocks.Pagination";

	internal static PaginationNullsInterceptor Instance { get; } = new();

	private static readonly Regex OrderByClause = new(
		@"\bORDER\s+BY\s+(?<body>.+?)(?=\s+(?:LIMIT|OFFSET|FETCH|FOR)\b|\s*$)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

	private static readonly Regex TagNulls = new(
		@"BuildingBlocks\.Pagination:(?<nulls>First|Last)",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	public override InterceptionResult<DbDataReader> ReaderExecuting(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result)
	{
		Rewrite(command, eventData);
		return base.ReaderExecuting(command, eventData, result);
	}

	public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result,
		CancellationToken cancellationToken = default)
	{
		Rewrite(command, eventData);
		return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
	}

	public override InterceptionResult<object> ScalarExecuting(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<object> result)
	{
		Rewrite(command, eventData);
		return base.ScalarExecuting(command, eventData, result);
	}

	public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<object> result,
		CancellationToken cancellationToken = default)
	{
		Rewrite(command, eventData);
		return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
	}

	internal static string TagFor(NullOrder nulls, bool walkBackward = false)
	{
		var effective = walkBackward
			? (nulls == NullOrder.Last ? NullOrder.First : NullOrder.Last)
			: nulls;
		return QueryTag + ":" + (effective == NullOrder.First ? "First" : "Last");
	}

	private static void Rewrite(DbCommand command, CommandEventData eventData)
	{
		var sql = command.CommandText;
		var match = TagNulls.Match(sql);
		if (!match.Success)
		{
			return;
		}

		var provider = eventData.Context?.Database.ProviderName;
		if (!CursorProvider.IsNpgsqlProviderName(provider)
			&& !string.Equals(provider, CursorProvider.Sqlite, StringComparison.Ordinal))
		{
			return;
		}

		var suffix = match.Groups["nulls"].Value.Equals("First", StringComparison.Ordinal)
			? " NULLS FIRST"
			: " NULLS LAST";
		command.CommandText = OrderByClause.Replace(
			sql,
			m => "ORDER BY " + RewriteOrderByBody(m.Groups["body"].Value, suffix),
			count: 1);
	}

	internal static string RewriteOrderByBody(string body, string nullsSuffix)
	{
		var parts = SplitOrderTerms(body);
		var sb = new StringBuilder(body.Length + parts.Count * nullsSuffix.Length);
		for (var i = 0; i < parts.Count; i++)
		{
			if (i > 0)
			{
				sb.Append(", ");
			}

			var term = parts[i].Trim();
			if (term.Length == 0)
			{
				continue;
			}

			sb.Append(term);
			if (!term.Contains("NULLS", StringComparison.OrdinalIgnoreCase))
			{
				sb.Append(nullsSuffix);
			}
		}

		return sb.ToString();
	}

	private static List<string> SplitOrderTerms(string body)
	{
		var parts = new List<string>();
		var depth = 0;
		var start = 0;
		for (var i = 0; i < body.Length; i++)
		{
			var c = body[i];
			if (c is '(')
			{
				depth++;
			}
			else if (c is ')')
			{
				depth = Math.Max(0, depth - 1);
			}
			else if (c is ',' && depth == 0)
			{
				parts.Add(body[start..i]);
				start = i + 1;
			}
		}

		parts.Add(body[start..]);
		return parts;
	}
}
