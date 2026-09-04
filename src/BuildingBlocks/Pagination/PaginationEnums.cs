namespace BuildingBlocks.Pagination;

/// <summary>Column sort direction in a <see cref="SortKey{T}"/>.</summary>
public enum SortDirection
{
	/// <summary>Ascending.</summary>
	Ascending = 0,

	/// <summary>Descending.</summary>
	Descending = 1
}

/// <summary>Walk direction relative to the sort key (first page vs last page, next vs previous).</summary>
public enum PageDirection
{
	/// <summary>Walk in the sort-key order (first page / next).</summary>
	Forward = 0,

	/// <summary>Walk opposite the sort-key order (last page / previous).</summary>
	Backward = 1
}

/// <summary>
/// Null placement for <c>string</c> (and other reference) sort slots.
/// Drives the seek predicate and, where supported, SQL <c>ORDER BY … NULLS FIRST/LAST</c>
/// (Dapper: PostgreSQL and Sqlite; EF: same providers when the host registers
/// <c>AddBuildingBlocksPagination</c> + <c>UseBuildingBlocksPagination</c>).
/// SQL Server does not emit <c>NULLS</c> (no portable index-friendly syntax).
/// Nullable value types (<c>int?</c>, <c>DateTime?</c>, …) are rejected at
/// <see cref="SortKey{T}"/> construction.
/// </summary>
public enum NullOrder
{
	/// <summary>Nulls after non-nulls (default).</summary>
	Last = 0,

	/// <summary>Nulls before non-nulls.</summary>
	First = 1
}

/// <summary>
/// Optional read hint. Default <see cref="None"/> is a no-op (no extra SQL or transaction).
/// </summary>
public enum QueryHint
{
	/// <summary>Provider default isolation. No extra statements.</summary>
	None = 0,

	/// <summary>
	/// SQL Server session isolation <c>READ UNCOMMITTED</c> (dirty reads). This is
	/// <strong>not</strong> table-hint <c>WITH (NOLOCK)</c>.
	/// EF: begins a <c>ReadUncommitted</c> transaction around COUNT (if requested) and PAGE
	/// when there is no ambient transaction; an ambient transaction is ignored (no nest).
	/// After commit/rollback the session is restored to <c>READ COMMITTED</c> while the
	/// connection remains open. Dapper: prefixes <c>SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;</c>
	/// on SQL Server only, then restores <c>READ COMMITTED</c> on the still-open connection.
	/// PostgreSQL and Sqlite are a no-op. Host <c>WITH (NOLOCK)</c> in Dapper SQL is
	/// still allowed when this is <see cref="None"/>.
	/// </summary>
	ReadUncommitted = 1
}

/// <summary>Typed pagination failure.</summary>
public enum PaginationErrorCode
{
	/// <summary>Cursor is missing, truncated, unsigned, or not JSON.</summary>
	InvalidCursor = 0,

	/// <summary>Limit is less than 1 or greater than <see cref="PaginationOptions.MaxLimit"/>.</summary>
	InvalidLimit = 1,

	/// <summary>Sort key does not end with a unique column.</summary>
	MissingUniqueKey = 2,

	/// <summary>Dapper execution requires a SQL identifier on every slot.</summary>
	MissingSqlIdentifier = 3,

	/// <summary>SQL identifier failed the allowlist pattern.</summary>
	InvalidIdentifier = 4,

	/// <summary>HMAC is enabled but the signing key is empty.</summary>
	SigningKeyRequired = 5,

	/// <summary>Cursor fingerprint does not match the requested sort key.</summary>
	CursorSortMismatch = 6,

	/// <summary>A projected DTO is missing a keyset column.</summary>
	MissingKeyColumn = 7,

	/// <summary>Shadow property name is not on the EF model.</summary>
	UnknownShadowProperty = 8,

	/// <summary>Dapper cannot execute a sort key that uses <c>ByShadow</c>.</summary>
	ShadowNotSupported = 9,

	/// <summary>Sort slot CLR type is not SQL-comparable (complex type, byte[], value object as a whole).</summary>
	UnsupportedSortType = 10,

	/// <summary>Dapper host SQL already contains ORDER BY / OFFSET / FETCH / LIMIT.</summary>
	InvalidHostSql = 11,

	/// <summary><c>HasKeysetIndex</c> cannot map a nested property path to store columns.</summary>
	UnsupportedKeysetIndex = 12,

	/// <summary>Nullable value type (<c>T?</c>) is not a keyset column.</summary>
	NullableSortUnsupported = 13
}
