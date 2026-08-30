using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildingBlocks.Pagination;

internal static class SqlIdentifier
{
	private static readonly Regex Pattern = new(
		@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	public static void EnsureValid(string identifier)
	{
		if (!Pattern.IsMatch(identifier))
		{
			throw new PaginationException(
				PaginationErrorCode.InvalidIdentifier,
				$"SQL identifier '{identifier}' is not allowlisted. Use unquoted [A-Za-z_][A-Za-z0-9_]* or schema.column.");
		}
	}

	public static string Fingerprint(IReadOnlyList<SortSlot> slots)
	{
		var sb = new StringBuilder();
		foreach (var slot in slots)
		{
			sb.Append(slot.FingerprintPart).Append(':')
				.Append(slot.Direction).Append(':')
				.Append(slot.ClrType.FullName).Append(':')
				.Append(slot.Kind).Append(';');
		}

		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
		return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
	}
}
