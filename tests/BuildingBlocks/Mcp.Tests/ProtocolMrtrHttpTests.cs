using System.Text;
using System.Text.Json;
using BuildingBlocks.Mcp;
using BuildingBlocks.Mcp.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace BuildingBlocks.Mcp.Tests;

/// <summary>
/// Wire-level Streamable HTTP tests of BuildingBlocks.Mcp's custom CallTool adapter.
/// These tests speak the official MCP C# client; they are not FeatureFusion/Mediator tests.
/// <para>
/// 2026-07-28: the adapter throws SDK <c>InputRequiredException</c>, which the server serializes as
/// <c>resultType: input_required</c> plus elicitation. The official client then auto-retries with
/// <c>inputResponses</c> / echoed <c>requestState</c> when <see cref="McpClientHandlers.ElicitationHandler"/>
/// is set. Without that handler the client throws and does not surface <see cref="InputRequiredResult"/> —
/// capture HTTP bodies to assert the incomplete result.
/// </para>
/// <para>
/// 2025-11-25: <c>IsMrtrSupported</c> is false, so the same unconfirmed write stays a
/// <see cref="McpErrorCode.ConfirmationRequired"/> tool error. <c>confirmed: true</c> skips MRTR on both revisions.
/// </para>
/// The HTTP transport is the SDK default (stateless). <c>requestState</c> is an opaque echo, not a server session.
/// </summary>
public sealed class ProtocolMrtrHttpTests
{
	private const string WriteTool = "tests.confirm-write";
	private const string ReadTool = "tests.ping";
	private const string July2026 = "2026-07-28";
	private const string November2025 = "2025-11-25";

	/// <summary>
	/// 2026-07-28 unconfirmed write: the adapter emits <c>input_required</c>;
	/// <see cref="McpClientHandlers.ElicitationHandler"/> accept retries with <c>inputResponses</c>; MapTool runs once.
	/// Wire capture is required because CallToolAsync does not return <see cref="InputRequiredResult"/>.
	/// </summary>
	[Fact]
	public async Task July2026_Unconfirmed_Write_Sends_InputRequired_Then_Accept_Invokes_Once()
	{
		var calls = new CallCounter();
		await using var host = await ProtocolHost.StartAsync(calls);
		var wire = new WireCapture();
		ElicitRequestParams? elicitation = null;
		await using var client = await host.ConnectAsync(July2026Accepting(req => elicitation = req), wire);

		var result = await client.CallToolAsync(
			WriteTool,
			WriteArgs(qty: 3, Guid.NewGuid().ToString("D"), confirmed: false));

		Assert.False(result.IsError ?? false);
		Assert.Contains("3", GetText(result), StringComparison.Ordinal);
		Assert.Equal(1, Volatile.Read(ref calls.Value));
		Assert.NotNull(elicitation);
		Assert.Contains("Confirm", elicitation!.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal("input_required", ParseInputRequired(wire.ResponseBodies).ResultType);
		Assert.Contains("inputResponses", wire.RequestBodies.Single(b => b.Contains("inputResponses", StringComparison.Ordinal)), StringComparison.Ordinal);
	}

	/// <summary>
	/// 2026-07-28 decline must follow elicitation on the wire (<c>resultType: input_required</c>).
	/// After decline the tool error is <see cref="McpErrorCode.ConfirmationRequired"/> and MapTool never runs.
	/// A bare ConfirmationRequired without elicitation is the 2025-11-25 fallback and must fail this test.
	/// </summary>
	[Fact]
	public async Task July2026_Unconfirmed_Write_Sends_InputRequired_Then_Decline_Does_Not_Invoke()
	{
		var calls = new CallCounter();
		await using var host = await ProtocolHost.StartAsync(calls);
		var wire = new WireCapture();
		ElicitRequestParams? elicitation = null;
		await using var client = await host.ConnectAsync(July2026Declining(req => elicitation = req), wire);

		var result = await client.CallToolAsync(
			WriteTool,
			WriteArgs(qty: 4, Guid.NewGuid().ToString("D"), confirmed: false));

		Assert.NotNull(elicitation);
		Assert.Equal("input_required", ParseInputRequired(wire.ResponseBodies).ResultType);
		Assert.True(result.IsError ?? false);
		Assert.Equal(McpErrorCode.ConfirmationRequired, ReadErrorCode(result));
		Assert.Equal(0, Volatile.Read(ref calls.Value));
	}

	/// <summary>
	/// Stateless Streamable HTTP: first client has no ElicitationHandler, so CallToolAsync throws.
	/// <c>requestState</c> is captured from the HTTP body and replayed on a new client with empty <c>SessionId</c>.
	/// The server does not keep MRTR state between connections.
	/// </summary>
	[Fact]
	public async Task July2026_InputRequired_Retry_On_A_New_Http_Client_Does_Not_Need_Server_Session()
	{
		var calls = new CallCounter();
		await using var host = await ProtocolHost.StartAsync(calls);
		var key = Guid.NewGuid().ToString("D");
		var args = WriteJsonArgs(qty: 7, key, confirmed: false);

		string? requestState;
		string inputKey;
		var firstWire = new WireCapture();
		await using (var first = await host.ConnectAsync(July2026Options(), firstWire))
		{
			Assert.True(string.IsNullOrEmpty(first.SessionId));
			// No ElicitationHandler: the SDK throws instead of returning InputRequiredResult.
			await Assert.ThrowsAsync<InvalidOperationException>(
				async () => await first.CallToolAsync(WriteTool, WriteArgs(qty: 7, key, confirmed: false)));
			var incomplete = ParseInputRequired(firstWire.ResponseBodies);
			Assert.Equal("input_required", incomplete.ResultType);
			Assert.False(string.IsNullOrWhiteSpace(incomplete.RequestState));
			requestState = incomplete.RequestState;
			inputKey = Assert.Single(incomplete.InputRequests!).Key;
			Assert.Equal(0, Volatile.Read(ref calls.Value));
		}

		await using var second = await host.ConnectAsync(July2026Options());
		Assert.True(string.IsNullOrEmpty(second.SessionId));
		var completed = await second.CallToolAsync(new CallToolRequestParams
		{
			Name = WriteTool,
			Arguments = args,
			RequestState = requestState,
			InputResponses = AcceptInput(inputKey)
		});

		Assert.False(completed.IsError ?? false);
		Assert.Equal(1, Volatile.Read(ref calls.Value));
	}

	/// <summary>
	/// 2025-11-25: <c>IsMrtrSupported</c> is false, so an unconfirmed write stays
	/// <see cref="McpErrorCode.ConfirmationRequired"/> JSON. No ElicitationHandler — MRTR is not negotiated.
	/// </summary>
	[Fact]
	public async Task November2025_Unconfirmed_Write_Returns_ConfirmationRequired_Not_InputRequired()
	{
		var calls = new CallCounter();
		await using var host = await ProtocolHost.StartAsync(calls);
		await using var client = await host.ConnectAsync(new McpClientOptions
		{
			ProtocolVersion = November2025
		});

		var result = await client.CallToolAsync(WriteTool, WriteArgs(qty: 2, Guid.NewGuid().ToString("D"), confirmed: false));
		Assert.True(result.IsError ?? false);
		Assert.Equal(McpErrorCode.ConfirmationRequired, ReadErrorCode(result));
		Assert.Equal(0, Volatile.Read(ref calls.Value));
	}

	/// <summary>
	/// <c>confirmed: true</c> is the pre-MRTR skip on 2026-07-28 as well.
	/// No ElicitationHandler: if the server emitted <c>input_required</c>, CallToolAsync would throw.
	/// </summary>
	[Fact]
	public async Task July2026_Confirmed_True_Skips_InputRequired_And_Invokes()
	{
		var calls = new CallCounter();
		await using var host = await ProtocolHost.StartAsync(calls);
		// No ElicitationHandler: success here proves the server did not emit input_required.
		await using var client = await host.ConnectAsync(July2026Options());

		var result = await client.CallToolAsync(
			WriteTool,
			WriteArgs(qty: 8, Guid.NewGuid().ToString("D"), confirmed: true));

		Assert.False(result.IsError ?? false);
		Assert.Equal(1, Volatile.Read(ref calls.Value));
	}

	/// <summary>
	/// MemoryIdempotencyStore is keyed by tool + idempotencyKey, not by <c>confirmed</c>.
	/// Accept (MRTR retry) then replay with <c>confirmed: true</c> must not invoke MapTool again.
	/// </summary>
	[Fact]
	public async Task July2026_Accepted_Write_Then_Same_Idempotency_Key_Does_Not_Invoke_Twice()
	{
		var calls = new CallCounter();
		await using var host = await ProtocolHost.StartAsync(calls);
		await using var client = await host.ConnectAsync(July2026Accepting());
		var key = Guid.NewGuid().ToString("D");

		var first = await client.CallToolAsync(WriteTool, WriteArgs(qty: 5, key, confirmed: false));
		Assert.False(first.IsError ?? false);
		Assert.Equal(1, Volatile.Read(ref calls.Value));

		var replay = await client.CallToolAsync(WriteTool, WriteArgs(qty: 99, key, confirmed: true));
		Assert.False(replay.IsError ?? false);
		Assert.Contains("5", GetText(replay), StringComparison.Ordinal);
		Assert.Equal(1, Volatile.Read(ref calls.Value));
	}

	/// <summary>
	/// Query tools are not <c>RequireConfirmation</c>. A 2026 ping must not elicit;
	/// no ElicitationHandler is registered so a stray <c>input_required</c> would throw.
	/// </summary>
	[Fact]
	public async Task July2026_Read_Tool_Does_Not_Use_InputRequired()
	{
		var calls = new CallCounter();
		await using var host = await ProtocolHost.StartAsync(calls);
		await using var client = await host.ConnectAsync(July2026Options());

		var result = await client.CallToolAsync(ReadTool, new Dictionary<string, object?> { ["name"] = "Ada" });
		Assert.False(result.IsError ?? false);
		Assert.Contains("pong:Ada", GetText(result), StringComparison.Ordinal);
		Assert.Equal(0, Volatile.Read(ref calls.Value));
	}

	/// <summary>
	/// McpInvoker rejects a missing idempotency key before confirmation/MRTR.
	/// Without a key there is no <c>input_required</c> and MapTool never runs.
	/// </summary>
	[Fact]
	public async Task July2026_Missing_Idempotency_Key_Fails_Before_InputRequired()
	{
		var calls = new CallCounter();
		await using var host = await ProtocolHost.StartAsync(calls);
		await using var client = await host.ConnectAsync(July2026Options());

		var result = await client.CallToolAsync(
			WriteTool,
			new Dictionary<string, object?> { ["qty"] = 1 });

		Assert.True(result.IsError ?? false);
		Assert.Equal(McpErrorCode.IdempotencyKeyRequired, ReadErrorCode(result));
		Assert.Equal(0, Volatile.Read(ref calls.Value));
	}

	/// <summary>Pins 2026-07-28 with no ElicitationHandler (CallToolAsync throws on <c>input_required</c>).</summary>
	private static McpClientOptions July2026Options()
		=> new() { ProtocolVersion = July2026 };

	/// <summary>2026 client that auto-accepts elicitation so CallToolAsync completes the MRTR retry.</summary>
	private static McpClientOptions July2026Accepting(Action<ElicitRequestParams>? onElicit = null)
		=> new()
		{
			ProtocolVersion = July2026,
			Handlers = new McpClientHandlers
			{
				ElicitationHandler = (request, _) =>
				{
					if (request is not null)
						onElicit?.Invoke(request);
					return ValueTask.FromResult(new ElicitResult
					{
						Action = "accept",
						Content = new Dictionary<string, JsonElement>
						{
							[McpDefaults.ConfirmedArgument] = JsonSerializer.SerializeToElement(true)
						}
					});
				}
			}
		};

	/// <summary>2026 client that declines elicitation; CallToolAsync then surfaces ConfirmationRequired.</summary>
	private static McpClientOptions July2026Declining(Action<ElicitRequestParams>? onElicit = null)
		=> new()
		{
			ProtocolVersion = July2026,
			Handlers = new McpClientHandlers
			{
				ElicitationHandler = (request, _) =>
				{
					if (request is not null)
						onElicit?.Invoke(request);
					return ValueTask.FromResult(new ElicitResult { Action = "decline" });
				}
			}
		};

	private static Dictionary<string, object?> WriteArgs(int qty, string key, bool confirmed)
	{
		var args = new Dictionary<string, object?>
		{
			["qty"] = qty,
			[McpDefaults.IdempotencyKeyArgument] = key
		};
		if (confirmed)
			args[McpDefaults.ConfirmedArgument] = true;
		return args;
	}

	private static Dictionary<string, JsonElement> WriteJsonArgs(int qty, string key, bool confirmed)
	{
		var args = new Dictionary<string, JsonElement>
		{
			["qty"] = JsonSerializer.SerializeToElement(qty),
			[McpDefaults.IdempotencyKeyArgument] = JsonSerializer.SerializeToElement(key)
		};
		if (confirmed)
			args[McpDefaults.ConfirmedArgument] = JsonSerializer.SerializeToElement(true);
		return args;
	}

	/// <summary>Builds the <c>inputResponses</c> map a second HTTP client would send after capturing <c>requestState</c>.</summary>
	private static Dictionary<string, InputResponse> AcceptInput(string inputKey)
		=> new()
		{
			[inputKey] = InputResponse.FromElicitResult(new ElicitResult
			{
				Action = "accept",
				Content = new Dictionary<string, JsonElement>
				{
					[McpDefaults.ConfirmedArgument] = JsonSerializer.SerializeToElement(true)
				}
			})
		};

	/// <summary>
	/// Reads <c>resultType: input_required</c> out of captured JSON-RPC/SSE bodies.
	/// The official client's CallToolAsync resolves MRTR internally and does not return <see cref="InputRequiredResult"/>.
	/// </summary>
	private static InputRequiredResult ParseInputRequired(IEnumerable<string> bodies)
	{
		foreach (var body in bodies)
		{
			foreach (var json in EnumerateJsonPayloads(body))
			{
				using var doc = JsonDocument.Parse(json);
				if (doc.RootElement.ValueKind != JsonValueKind.Object)
					continue;
				if (!doc.RootElement.TryGetProperty("result", out var result))
					continue;
				if (!result.TryGetProperty("resultType", out var resultType)
					|| resultType.GetString() != "input_required")
					continue;
				return result.Deserialize<InputRequiredResult>(McpJsonUtilities.DefaultOptions)
					?? throw new InvalidOperationException("Failed to deserialize InputRequiredResult.");
			}
		}

		throw new InvalidOperationException("No input_required JSON-RPC result was captured.");
	}

	private static IEnumerable<string> EnumerateJsonPayloads(string body)
	{
		var trimmed = body.Trim();
		if (trimmed.StartsWith('{'))
		{
			yield return trimmed;
			yield break;
		}

		foreach (var line in body.Split('\n'))
		{
			var data = line.Trim();
			if (data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
				yield return data["data:".Length..].Trim();
		}
	}

	private static string GetText(CallToolResult result)
		=> string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));

	private static McpErrorCode ReadErrorCode(CallToolResult result)
	{
		using var doc = JsonDocument.Parse(GetText(result));
		var code = doc.RootElement.GetProperty("code");
		if (code.ValueKind == JsonValueKind.Number)
			return (McpErrorCode)code.GetInt32();
		return Enum.Parse<McpErrorCode>(code.GetString()!);
	}

	/// <summary>Counts MapTool handler executions, not HTTP round trips.</summary>
	internal sealed class CallCounter
	{
		public int Value;
	}
}

/// <summary>
/// Copies Streamable HTTP request/response bodies. The official client hides <c>input_required</c>
/// behind ElicitationHandler auto-retry, so protocol assertions read these captures.
/// </summary>
file sealed class WireCapture : DelegatingHandler
{
	public List<string> RequestBodies { get; } = [];
	public List<string> ResponseBodies { get; } = [];

	public WireCapture()
		: base(new SocketsHttpHandler())
	{
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Content is not null)
			RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

		var response = await base.SendAsync(request, cancellationToken);
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		ResponseBodies.Add(Encoding.UTF8.GetString(bytes));

		var copy = new ByteArrayContent(bytes);
		foreach (var header in response.Content.Headers)
			copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
		response.Content = copy;
		return response;
	}
}

/// <summary>
/// In-process Kestrel + <c>MapBuildingBlocksMcp</c> only. Not FeatureFusion, not Mediator.
/// </summary>
file sealed class ProtocolHost : IAsyncDisposable
{
	private readonly WebApplication _app;

	private ProtocolHost(WebApplication app) => _app = app;

	public static async Task<ProtocolHost> StartAsync(ProtocolMrtrHttpTests.CallCounter calls)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Services.AddBuildingBlocksMcp(o =>
		{
			o.UseMemoryIdempotency(TimeSpan.FromHours(1));
			o.MapTool<ConfirmWrite, int>(
				"tests.confirm-write",
				"Confirm write",
				(msg, _, _) =>
				{
					Interlocked.Increment(ref calls.Value);
					return Task.FromResult(McpResult.Ok(msg.Qty));
				},
				a =>
				{
					a.Kind = McpToolKind.Command;
					a.Idempotent = true;
					a.RequireConfirmation = true;
				});
			o.MapTool<PingQuery, string>(
				"tests.ping",
				"Ping",
				(msg, _, _) => Task.FromResult(McpResult.Ok(string.IsNullOrWhiteSpace(msg.Name) ? "pong" : $"pong:{msg.Name}")),
				a => a.Kind = McpToolKind.Query);
		});

		var app = builder.Build();
		app.MapBuildingBlocksMcp();
		await app.StartAsync();
		return new ProtocolHost(app);
	}

	public async Task<McpClient> ConnectAsync(McpClientOptions options, WireCapture? wire = null)
	{
		HttpMessageHandler pipeline = wire is not null ? wire : new SocketsHttpHandler();
		var http = new HttpClient(pipeline) { BaseAddress = new Uri(_app.Urls.Single()) };
		var transport = new HttpClientTransport(
			new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress, "mcp") },
			http,
			ownsHttpClient: true);
		return await McpClient.CreateAsync(transport, options);
	}

	public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

file sealed class ConfirmWrite
{
	public int Qty { get; set; }
}

file sealed class PingQuery
{
	public string? Name { get; set; }
}
