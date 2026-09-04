namespace BuildingBlocks.Idempotency;

/// <summary>
/// Wire format stored in <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
/// Status values: <c>Processing</c>, <c>Completed</c> (and read-compat <c>Failed</c>).
/// </summary>
public sealed class IdempotencyCacheEntry
{
	/// <summary>Lifecycle status string.</summary>
	public string Status { get; set; } = string.Empty;

	/// <summary>Serialized response body when completed (or read-compat Failed).</summary>
	public string? Response { get; set; }

	/// <summary>HTTP status to replay. Null on legacy entries → treat as 200.</summary>
	public int? StatusCode { get; set; }

	/// <summary>Content-Type to replay. Null on legacy entries → <c>application/json</c>.</summary>
	public string? ContentType { get; set; }

	/// <summary>
	/// SHA-256 hex of <c>method + "\n" + path + "\n" + body</c> when fingerprinting was enabled on first miss.
	/// </summary>
	public string? RequestFingerprint { get; set; }

	/// <summary>UTC instant after which a Processing entry is considered abandoned.</summary>
	public DateTimeOffset? ProcessingExpiresAtUtc { get; set; }
}
