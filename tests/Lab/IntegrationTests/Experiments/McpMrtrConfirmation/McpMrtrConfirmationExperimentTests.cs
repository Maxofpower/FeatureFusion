using System.Text.Json;
using BuildingBlocks.Mcp;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.McpMrtrConfirmation;

/// <summary>
/// Lab prototype (not a numbered Exp): FeatureFusion application integration of MCP 2026-07-28
/// confirmation MRTR. Protocol wire shape is proven in <c>BuildingBlocks.Mcp.Tests.ProtocolMrtrHttpTests</c>;
/// this suite asks whether the existing Mediator <c>orders.create</c> path is the only execution path.
/// <para>
/// Hypothesis: a 2026 client without <c>confirmed: true</c> elicits confirmation; accept reaches
/// <c>CreateOrderCommand</c> once; decline never Sends; the same idempotency key does not write twice;
/// a 2025-11-25 client still uses the Exp 6 <c>ConfirmationRequired</c> / <c>confirmed: true</c> fallback.
/// The host is SDK-stateless Streamable HTTP — <c>requestState</c> is echoed, not a FeatureFusion session.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpMrtrConfirmationExperimentTests
{
	private const string ToolName = "orders.create";
	private const int ProductId = 1;
	private const int CustomerId = 1;
	private const int Quantity = 2;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _http;
	private readonly ITestOutputHelper _output;

	public McpMrtrConfirmationExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_output = output;
		_http = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	/// <summary>
	/// A — 2026-07-28 accept: elicitation must fire (not a bare ConfirmationRequired).
	/// After accept, FeatureFusion Sends <c>CreateOrderCommand</c> and returns an order id.
	/// </summary>
	[Fact]
	public async Task A_July2026_Accept_After_Elicitation_Reaches_CreateOrderCommand()
	{
		using var capture = new InProcessActivityCapture();
		ElicitRequestParams? elicitation = null;
		await using var mcp = await LabMcpClient.CreateJuly2026Async(_http, AcceptConfirmation(req => elicitation = req));
		var before = MediatorCount(capture);

		var completed = await mcp.CallToolAsync(
			ToolName,
			OrderArgs(System.Ulid.NewUlid().ToString(), confirmed: false));

		(completed.IsError ?? false).Should().BeFalse(McpToolResults.GetText(completed));
		elicitation.Should().NotBeNull("2026 unconfirmed writes must elicit; ConfirmationRequired alone is the 2025 fallback");
		elicitation!.Message.Should().Contain("Confirm");
		var order = McpToolResults.TryParseOrder(completed, JsonOptions);
		order.Should().NotBeNull();
		order!.OrderId.Should().NotBeEmpty();
		MediatorCount(capture).Should().BeGreaterThan(before);
		_output.WriteLine(McpToolResults.GetText(completed));
	}

	/// <summary>
	/// B — 2026-07-28 decline: <see cref="McpClientHandlers.ElicitationHandler"/> must run first.
	/// Then the tool error is ConfirmationRequired and Mediator span count does not increase.
	/// Passing on ConfirmationRequired alone would be the 2025 fallback.
	/// </summary>
	[Fact]
	public async Task B_July2026_Decline_After_Elicitation_Does_Not_Send_CreateOrderCommand()
	{
		using var capture = new InProcessActivityCapture();
		ElicitRequestParams? elicitation = null;
		await using var mcp = await LabMcpClient.CreateJuly2026Async(_http, DeclineConfirmation(req => elicitation = req));
		var before = MediatorCount(capture);

		var declined = await mcp.CallToolAsync(
			ToolName,
			OrderArgs(System.Ulid.NewUlid().ToString(), confirmed: false));

		elicitation.Should().NotBeNull("decline must follow input_required elicitation, not a bare ConfirmationRequired error");
		(declined.IsError ?? false).Should().BeTrue();
		McpToolResults.TryReadJsonErrorCode(declined).Should().Be(nameof(McpErrorCode.ConfirmationRequired));
		MediatorCount(capture).Should().Be(before);
	}

	/// <summary>
	/// C — 2025-11-25 FeatureFusion path (same pin as Exp 6): unconfirmed stays ConfirmationRequired JSON;
	/// <c>confirmed: true</c> still Sends. No ElicitationHandler because MRTR is not negotiated.
	/// </summary>
	[Fact]
	public async Task C_November2025_Unconfirmed_Stays_ConfirmationRequired_Confirmed_True_Still_Sends()
	{
		using var capture = new InProcessActivityCapture();
		await using var mcp = await LabMcpClient.CreateNovember2025Async(_http);
		var before = MediatorCount(capture);
		var result = await mcp.CallToolAsync(
			ToolName,
			OrderArgs(System.Ulid.NewUlid().ToString(), confirmed: false));

		(result.IsError ?? false).Should().BeTrue();
		McpToolResults.TryReadJsonErrorCode(result).Should().Be(nameof(McpErrorCode.ConfirmationRequired));
		MediatorCount(capture).Should().Be(before);

		var confirmed = await mcp.CallToolAsync(
			ToolName,
			OrderArgs(System.Ulid.NewUlid().ToString(), confirmed: true));
		(confirmed.IsError ?? false).Should().BeFalse(McpToolResults.GetText(confirmed));
		MediatorCount(capture).Should().BeGreaterThan(before);
	}

	/// <summary>
	/// D — after an accepted 2026 elicitation, replaying the same MemoryIdempotencyStore key
	/// (even with <c>confirmed: true</c>) returns the same order id and does not Send again.
	/// </summary>
	[Fact]
	public async Task D_July2026_Same_Idempotency_Key_After_Accepted_Elicitation_Does_Not_Write_Twice()
	{
		using var capture = new InProcessActivityCapture();
		ElicitRequestParams? elicitation = null;
		await using var mcp = await LabMcpClient.CreateJuly2026Async(_http, AcceptConfirmation(req => elicitation = req));
		var key = System.Ulid.NewUlid().ToString();
		var before = MediatorCount(capture);

		var first = await mcp.CallToolAsync(ToolName, OrderArgs(key, confirmed: false));
		(first.IsError ?? false).Should().BeFalse(McpToolResults.GetText(first));
		elicitation.Should().NotBeNull();
		var order = McpToolResults.TryParseOrder(first, JsonOptions);
		order.Should().NotBeNull();
		var afterAccept = MediatorCount(capture);
		afterAccept.Should().BeGreaterThan(before);

		var replay = await mcp.CallToolAsync(ToolName, OrderArgs(key, confirmed: true));
		(replay.IsError ?? false).Should().BeFalse(McpToolResults.GetText(replay));
		var replayed = McpToolResults.TryParseOrder(replay, JsonOptions);
		replayed.Should().NotBeNull();
		replayed!.OrderId.Should().Be(order!.OrderId);
		MediatorCount(capture).Should().Be(afterAccept);
	}

	/// <summary>
	/// E — FeatureFusion <c>/mcp</c> is SDK-stateless Streamable HTTP.
	/// Empty <c>SessionId</c> after accept shows confirmation is not stored in a transport session.
	/// Protocol-level <c>requestState</c> replay is in <c>ProtocolMrtrHttpTests</c>.
	/// </summary>
	[Fact]
	public async Task E_July2026_Accepted_CreateOrder_Does_Not_Require_An_Mcp_Session()
	{
		using var capture = new InProcessActivityCapture();
		await using var mcp = await LabMcpClient.CreateJuly2026Async(_http, AcceptConfirmation());
		mcp.SessionId.Should().BeNullOrEmpty("stateless Streamable HTTP has no transport session to hold MRTR state");
		var before = MediatorCount(capture);

		var completed = await mcp.CallToolAsync(
			ToolName,
			OrderArgs(System.Ulid.NewUlid().ToString(), confirmed: false));

		(completed.IsError ?? false).Should().BeFalse(McpToolResults.GetText(completed));
		mcp.SessionId.Should().BeNullOrEmpty();
		MediatorCount(capture).Should().BeGreaterThan(before);
	}

	/// <summary>
	/// Control: <c>lab.ping</c> is a query. A 2026 client without ElicitationHandler must still succeed
	/// (a stray <c>input_required</c> would throw).
	/// </summary>
	[Fact]
	public async Task Read_Only_Lab_Ping_Is_Unaffected()
	{
		await using var mcp = await LabMcpClient.CreateJuly2026Async(_http);
		var result = await mcp.CallToolAsync(
			"lab.ping",
			new Dictionary<string, object?> { ["name"] = "Ada" });
		(result.IsError ?? false).Should().BeFalse();
		McpToolResults.GetText(result).Should().Contain("pong:Ada");
	}

	private static Dictionary<string, object?> OrderArgs(string idempotencyKey, bool confirmed)
	{
		var args = new Dictionary<string, object?>
		{
			["productId"] = ProductId,
			["quantity"] = Quantity,
			["customerId"] = CustomerId,
			[McpDefaults.IdempotencyKeyArgument] = idempotencyKey
		};
		if (confirmed)
			args[McpDefaults.ConfirmedArgument] = true;
		return args;
	}

	/// <summary>
	/// Official-client elicitation callback. CallToolAsync auto-retries with inputResponses after this returns.
	/// </summary>
	private static McpClientHandlers AcceptConfirmation(Action<ElicitRequestParams>? onElicit = null)
		=> new()
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
		};

	/// <summary>ElicitationHandler that declines so CallToolAsync surfaces ConfirmationRequired without Send.</summary>
	private static McpClientHandlers DeclineConfirmation(Action<ElicitRequestParams>? onElicit = null)
		=> new()
		{
			ElicitationHandler = (request, _) =>
			{
				if (request is not null)
					onElicit?.Invoke(request);
				return ValueTask.FromResult(new ElicitResult { Action = "decline" });
			}
		};

	private static int MediatorCount(InProcessActivityCapture capture)
		=> capture.All.Count(IsMediator);
}
