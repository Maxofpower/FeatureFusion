using System.Text.Json.Serialization;

namespace IntegrationTests.Experiments.PaginationAbuse;

internal sealed record PaginationAbuseExperimentResult(
	string Name,
	DateTimeOffset StartedUtc,
	string GitSha,
	string Environment,
	int CatalogProductCount,
	PaginationAbuseConfiguration Configuration,
	IReadOnlyList<PaginationAbuseCall> Calls,
	PaginationAbuseObservations Observations);

internal sealed record PaginationAbuseConfiguration(
	string Path,
	string SortBy,
	string SortDirection,
	int Limit,
	int WalkPages);

internal sealed record PaginationAbuseCall(
	int RequestNumber,
	string Behavior,
	int Limit,
	string SortBy,
	string SortDirection,
	string PageDirection,
	string CursorIn,
	int HttpStatus,
	IReadOnlyList<int> ItemIds,
	string NextCursor,
	string PreviousCursor,
	bool HasMore,
	bool HasPrevious,
	int TotalCount,
	long ClientDurationMs,
	string? Error,
	string TraceId,
	int MediatorSpanCount,
	double? MediatorDurationMs,
	int NpgsqlSpanCount);

internal sealed record PaginationAbuseObservations(
	int WalkCalls,
	int WalkUniqueIds,
	int WalkDuplicateIds,
	int WalkEmptyPages,
	bool ReplaySameIdsAsOriginal,
	int ReplayDuplicateIdCount,
	bool StaleReuseSameIdsAsOriginalPage,
	int TamperHttpStatus,
	IReadOnlyList<int> TamperReturnedIds,
	int MalformedHttpStatus,
	bool CursorLoopDetected,
	int TotalCalls,
	int FailureCount,
	long TotalClientDurationMs,
	IReadOnlyList<string> Notes);

internal sealed record ProductsPage(
	IReadOnlyList<ProductItem> Items,
	string NextCursor,
	string PreviousCursor,
	bool HasMore,
	bool HasPrevious,
	int TotalCount);

internal sealed record ProductItem(int Id, string Name, decimal Price, DateTime CreatedAt);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PaginationAbuseExperimentResult))]
internal partial class PaginationAbuseJsonContext : JsonSerializerContext;
