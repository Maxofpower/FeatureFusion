using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Pagination.EntityFrameworkCore.Infrastructure.Internal;

/// <summary>Soft provider-name checks (no PackageReference on Npgsql/Sqlite/SqlServer).</summary>
internal static class CursorProvider
{
	internal const string Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";
	internal const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";
	internal const string SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

	public static bool IsNpgsql(DbContext? ctx)
		=> ctx is not null
			&& string.Equals(ctx.Database.ProviderName, Npgsql, StringComparison.Ordinal);

	public static bool IsSqlite(DbContext? ctx)
		=> ctx is not null
			&& string.Equals(ctx.Database.ProviderName, Sqlite, StringComparison.Ordinal);

	public static bool SupportsOrderByNulls(DbContext? ctx)
		=> IsNpgsql(ctx) || IsSqlite(ctx);

	public static bool IsNpgsqlProviderName(string? providerName)
		=> string.Equals(providerName, Npgsql, StringComparison.Ordinal);
}
