using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace IntegrationTests.Experiments.PaginationAbuse;

/// <summary>
/// Deterministic HTTP client that pages like a careless tool caller.
/// Forgery uses public Base64url + JSON only — not <c>CursorCodec.Encode</c>.
/// </summary>
internal sealed class CarelessPaginationClient
{
	internal const string Path = "/api/v2/products-page";
	internal const string SortBy = "Id";
	internal const string SortDirection = "Ascending";
	internal const string PageDirection = "Forward";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;

	public CarelessPaginationClient(HttpClient http) => _http = http;

	public async Task<PaginationAbuseHttpResult> GetAsync(
		int limit,
		string cursor,
		string pageDirection,
		string traceparent)
	{
		var url = $"{Path}?limit={limit}&sortBy={SortBy}&sortDirection={SortDirection}&pageDirection={pageDirection}";
		if (!string.IsNullOrEmpty(cursor))
			url += $"&cursor={Uri.EscapeDataString(cursor)}";

		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.TryAddWithoutValidation("traceparent", traceparent);

		var clock = Stopwatch.StartNew();
		var response = await _http.SendAsync(request);
		clock.Stop();

		var body = await response.Content.ReadAsStringAsync();
		ProductsPage? page = null;
		string? error = null;
		if (response.IsSuccessStatusCode)
		{
			page = JsonSerializer.Deserialize<ProductsPage>(body, JsonOptions);
		}
		else
		{
			error = body.Length > 500 ? body[..500] : body;
		}

		return new PaginationAbuseHttpResult(
			(int)response.StatusCode,
			page,
			clock.ElapsedMilliseconds,
			error);
	}

	/// <summary>
	/// Decode <c>v1.{"{payload}"}</c>, bump the Id-sort seek integer, re-encode unsigned.
	/// For <c>sortBy=Id</c> the payload has one <c>vals</c> slot (<c>t=System.Int32</c>).
	/// </summary>
	public static string TamperSeekId(string cursor, int delta)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(cursor);
		if (!cursor.StartsWith("v1.", StringComparison.Ordinal))
			throw new InvalidOperationException($"Cursor is not v1: {cursor[..Math.Min(cursor.Length, 24)]}");

		var rest = cursor[3..];
		var dot = rest.IndexOf('.');
		var payload = dot < 0 ? rest : rest[..dot];
		var json = Encoding.UTF8.GetString(Base64UrlDecode(payload));
		using var doc = JsonDocument.Parse(json);
		if (!doc.RootElement.TryGetProperty("vals", out var vals)
			|| vals.ValueKind != JsonValueKind.Array
			|| vals.GetArrayLength() < 1)
		{
			throw new InvalidOperationException($"Cursor payload has no vals: {json}");
		}

		var slot = vals[0];
		if (!slot.TryGetProperty("v", out var seek) || seek.ValueKind != JsonValueKind.Number)
			throw new InvalidOperationException($"Id-sort seek value is not a number: {json}");

		var original = seek.GetInt32();
		var forged = original + delta;

		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			CopyProperty(writer, doc.RootElement, "v");
			CopyProperty(writer, doc.RootElement, "fp");
			CopyProperty(writer, doc.RootElement, "walk");
			writer.WritePropertyName("vals");
			writer.WriteStartArray();
			writer.WriteStartObject();
			CopyProperty(writer, slot, "t");
			writer.WriteNumber("v", forged);
			writer.WriteEndObject();
			for (var i = 1; i < vals.GetArrayLength(); i++)
				vals[i].WriteTo(writer);
			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		return "v1." + Base64UrlEncode(stream.ToArray());
	}

	private static void CopyProperty(Utf8JsonWriter writer, JsonElement obj, string name)
	{
		if (!obj.TryGetProperty(name, out var value))
			return;
		writer.WritePropertyName(name);
		value.WriteTo(writer);
	}

	private static string Base64UrlEncode(byte[] data)
		=> Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static byte[] Base64UrlDecode(string text)
	{
		var padded = text.Replace('-', '+').Replace('_', '/');
		padded += (4 - padded.Length % 4) % 4 == 0 ? "" : new string('=', (4 - padded.Length % 4) % 4);
		return Convert.FromBase64String(padded);
	}
}

internal sealed record PaginationAbuseHttpResult(
	int HttpStatus,
	ProductsPage? Page,
	long ClientDurationMs,
	string? Error);
