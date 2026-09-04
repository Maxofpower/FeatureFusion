using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IntegrationTests.Infrastructure.Telemetry;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Infrastructure.Orders;

/// <summary>
/// Lab-only HTTP POST to FeatureFusion order create. Observation only — no assertions.
/// </summary>
public static class HttpOrderCreate
{
	public const string Path = "/api/v2/Order/order";
	public const string IdempotencyHeader = "Idempotency-Key";
	public const string CachedResponseHeader = "X-Idempotent-Response";

	private static readonly JsonSerializerOptions DefaultJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	/// <summary>
	/// Posts <c>{"productId","quantity","customerId"}</c> with Idempotency-Key and W3C traceparent.
	/// When <paramref name="traceId"/> / <paramref name="parentSpanId"/> are omitted, a new trace parent is created
	/// (same as prior experiment-local <c>NewTraceParent()</c> usage).
	/// </summary>
	public static async Task<HttpOrderCreateResult> PostAsync(
		HttpClient http,
		InProcessActivityCapture capture,
		string idempotencyKey,
		int quantity,
		int productId = 1,
		int customerId = 1,
		ActivityTraceId? traceId = null,
		ActivitySpanId? parentSpanId = null,
		JsonSerializerOptions? jsonOptions = null,
		CancellationToken cancellationToken = default)
	{
		ActivityTraceId tid;
		ActivitySpanId sid;
		if (traceId is { } t && parentSpanId is { } s)
		{
			tid = t;
			sid = s;
		}
		else
		{
			(tid, sid) = NewTraceParent();
		}

		using var request = new HttpRequestMessage(HttpMethod.Post, Path);
		request.Headers.TryAddWithoutValidation(IdempotencyHeader, idempotencyKey);
		request.Headers.TryAddWithoutValidation("traceparent", FormatTraceParent(tid, sid));
		request.Content = new StringContent(
			$$"""{"productId":{{productId}},"quantity":{{quantity}},"customerId":{{customerId}}}""",
			Encoding.UTF8,
			"application/json");

		var started = Stopwatch.StartNew();
		using var response = await http.SendAsync(request, cancellationToken);
		started.Stop();
		var completedUtc = DateTimeOffset.UtcNow;

		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		HttpOrderBody? order = null;
		if (response.IsSuccessStatusCode)
		{
			order = JsonSerializer.Deserialize<HttpOrderBody>(
				body,
				jsonOptions ?? DefaultJsonOptions);
		}

		var spans = capture.ForTrace(tid);
		return new HttpOrderCreateResult(
			HttpStatus: (int)response.StatusCode,
			Body: body,
			OrderId: order?.OrderId ?? Guid.Empty,
			Quantity: order?.Quantity,
			TotalAmount: order?.TotalAmount,
			CachedResponseHeader: response.Headers.Contains(CachedResponseHeader),
			ClientDurationMs: started.Elapsed.TotalMilliseconds,
			CompletedUtc: completedUtc,
			TraceId: tid,
			TraceIdHex: tid.ToHexString(),
			Spans: spans);
	}
}

public sealed record HttpOrderBody(Guid OrderId, int Quantity, decimal TotalAmount);

public sealed record HttpOrderCreateResult(
	int HttpStatus,
	string Body,
	Guid OrderId,
	int? Quantity,
	decimal? TotalAmount,
	bool CachedResponseHeader,
	double ClientDurationMs,
	DateTimeOffset CompletedUtc,
	ActivityTraceId TraceId,
	string TraceIdHex,
	IReadOnlyList<CapturedActivity> Spans);
