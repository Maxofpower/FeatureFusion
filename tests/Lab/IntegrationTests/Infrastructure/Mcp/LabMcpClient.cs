using ModelContextProtocol.Client;

namespace IntegrationTests.Infrastructure.Mcp;

/// <summary>
/// Official MCP C# client against FeatureFusion <c>/mcp</c>.
/// <para>
/// <see cref="CreateAsync(HttpClient)"/> leaves <see cref="McpClientOptions.ProtocolVersion"/> unset
/// (SDK probes and prefers 2026-07-28). MRTR and down-level confirmation tests must pin the revision
/// explicitly via <see cref="CreateJuly2026Async"/> or <see cref="CreateNovember2025Async"/>.
/// </para>
/// </summary>
public static class LabMcpClient
{
	/// <summary>
	/// MCP 2025-11-25: initialize handshake, no MRTR.
	/// Unconfirmed <c>RequireConfirmation</c> writes return <c>ConfirmationRequired</c> JSON, not <c>input_required</c>.
	/// </summary>
	public const string November2025ProtocolVersion = "2025-11-25";

	/// <summary>
	/// MCP 2026-07-28: stateless Streamable HTTP.
	/// Unconfirmed <c>RequireConfirmation</c> writes return MRTR <c>input_required</c> when the server throws
	/// <c>InputRequiredException</c>. The official client auto-retries only if
	/// <see cref="McpClientHandlers.ElicitationHandler"/> is registered; otherwise it throws and does not
	/// surface <c>InputRequiredResult</c>.
	/// </summary>
	public const string July2026ProtocolVersion = "2026-07-28";

	/// <summary>
	/// Unpinned protocol: the SDK probes and typically selects 2026-07-28.
	/// Use <see cref="CreateJuly2026Async"/> or <see cref="CreateNovember2025Async"/> when the revision is part of the assertion.
	/// </summary>
	public static Task<McpClient> CreateAsync(HttpClient http)
		=> CreateAsync(http, clientOptions: null);

	/// <summary>
	/// Pins 2026-07-28 so tests do not depend on the SDK default-version probe.
	/// Pass handlers when the call is expected to elicit (accept/decline confirmation).
	/// </summary>
	public static Task<McpClient> CreateJuly2026Async(HttpClient http, McpClientHandlers? handlers = null)
	{
		var options = new McpClientOptions { ProtocolVersion = July2026ProtocolVersion };
		if (handlers is not null)
			options.Handlers = handlers;
		return CreateAsync(http, options);
	}

	/// <summary>
	/// Pins 2025-11-25 so MRTR is not negotiated.
	/// Exp 6 Unconfirmed must keep observing <c>ConfirmationRequired</c>; a 2026 client would get <c>input_required</c> instead.
	/// </summary>
	public static Task<McpClient> CreateNovember2025Async(HttpClient http)
		=> CreateAsync(http, new McpClientOptions { ProtocolVersion = November2025ProtocolVersion });

	/// <summary>
	/// Connects the official C# client to FeatureFusion <c>/mcp</c>.
	/// Null options leave <see cref="McpClientOptions.ProtocolVersion"/> unset (SDK default probe).
	/// Do not set <see cref="McpClientOptions.Handlers"/> to null — the SDK setter throws.
	/// </summary>
	public static async Task<McpClient> CreateAsync(HttpClient http, McpClientOptions? clientOptions)
	{
		var endpoint = new Uri(http.BaseAddress ?? new Uri("http://localhost"), "mcp");
		var transport = new HttpClientTransport(
			new HttpClientTransportOptions { Endpoint = endpoint },
			http,
			ownsHttpClient: false);
		return await McpClient.CreateAsync(transport, clientOptions);
	}
}
