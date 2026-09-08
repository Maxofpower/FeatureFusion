using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FeatureFusion.Features.Admission;
using FeatureFusion.Features.Order.IntegrationEvents.Events;
using FeatureFusion.Features.Orders.Commands;
using FeatureFusion.Infrastructure.Context;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Async;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Orders;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Admission;

/// <summary>
/// Product-slice proof: application-owned Defer-before-Send for <c>orders.create</c>.
/// Not Exp 21 — separate from the closed MCP/EventBus research line.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class OrderCreateAdmissionTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly ITestOutputHelper _output;
	private readonly WebApplicationFactory<Program> _deferHost;
	private readonly HttpClient _http;

	public OrderCreateAdmissionTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_deferHost = fixture.WithWebHostBuilder(builder =>
		{
			builder.ConfigureTestServices(services =>
			{
				services.PostConfigure<CapabilityAdmissionOptions>(o =>
				{
					o.DeferredCapabilities.Clear();
					o.DeferredCapabilities.Add(CapabilityIds.OrdersCreate);
					o.TicketTtl = TimeSpan.FromHours(1);
				});
			});
		});
		_http = _deferHost.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Http_create_defers_without_send_order_or_outbox()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var outboxBefore = await CountOrderCreatedOutboxAsync();

		var result = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 2);
		var pending = ParsePending(result.Body);

		result.HttpStatus.Should().Be((int)HttpStatusCode.Accepted);
		pending.TicketId.Should().NotBeEmpty();
		pending.CapabilityId.Should().Be(CapabilityIds.OrdersCreate);
		pending.Status.Should().Be("Pending");
		result.OrderId.Should().Be(Guid.Empty);

		CreateOrderMediatorCount(result.Spans).Should().Be(0, "Defer must not call ISender.Send");
		(await CountOrderCreatedOutboxAsync()).Should().Be(outboxBefore, "no outbox on Defer");
		(await GetTicketAsync(pending.TicketId))!.Status.Should().Be("Pending");
	}

	[Fact]
	public async Task Mcp_confirmed_create_still_defers_without_send_order_or_outbox()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var outboxBefore = await CountOrderCreatedOutboxAsync();

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		capture.Clear();
		var call = await mcp.CallToolAsync(
			"orders.create",
			new Dictionary<string, object?>
			{
				["productId"] = 1,
				["quantity"] = 2,
				["customerId"] = 1,
				["confirmed"] = true,
				["idempotencyKey"] = key
			});

		(call.IsError ?? false).Should().BeFalse("confirmed=true must not be treated as release");
		var pending = ParsePendingFromMcp(call);
		pending.TicketId.Should().NotBeEmpty();
		pending.Status.Should().Be("Pending");

		capture.All.Count(IsMediator).Should().Be(0, "no Mediator Send on Defer");
		capture.All.Count(s => s.DisplayName == "mcp.tool").Should().BeGreaterThan(0,
			"MCP InvokeCore still runs; admission is inside the dispatcher");
		(await CountOrderCreatedOutboxAsync()).Should().Be(outboxBefore);
	}

	[Fact]
	public async Task Http_and_mcp_share_the_same_admission_tickets()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();

		var http = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 3);
		var httpPending = ParsePending(http.Body);

		await using var mcp = await LabMcpClient.CreateAsync(_http);
		var mcpCall = await mcp.CallToolAsync(
			"orders.create",
			new Dictionary<string, object?>
			{
				["productId"] = 1,
				["quantity"] = 3,
				["customerId"] = 1,
				["confirmed"] = true,
				["idempotencyKey"] = key
			});
		var mcpPending = ParsePendingFromMcp(mcpCall);

		mcpPending.TicketId.Should().Be(httpPending.TicketId,
			"same request key must resolve to the same durable ticket across HTTP and MCP");
	}

	[Fact]
	public async Task Repeated_request_while_pending_returns_same_ticket_no_order()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var outboxBefore = await CountOrderCreatedOutboxAsync();

		var first = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 2);
		var second = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 2);
		var a = ParsePending(first.Body);
		var b = ParsePending(second.Body);

		a.TicketId.Should().Be(b.TicketId);
		first.OrderId.Should().Be(Guid.Empty);
		second.OrderId.Should().Be(Guid.Empty);
		(await CountOrderCreatedOutboxAsync()).Should().Be(outboxBefore);

		await using var scope = _deferHost.Services.CreateAsyncScope();
		var count = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
			.IntentTickets.CountAsync(t => t.RequestKey == key);
		count.Should().Be(1, "identity: CapabilityId + RequestKey → one Pending ticket");
	}

	[Fact]
	public async Task Same_request_key_different_payload_is_rejected_at_admission_when_admit_runs()
	{
		await using var scope = _deferHost.Services.CreateAsyncScope();
		var admission = scope.ServiceProvider.GetRequiredService<ICapabilityAdmission>();
		var key = Ulid.NewUlid().ToString();

		var cmdA = new CreateOrderCommand { ProductId = 1, Quantity = 2, CustomerId = 1 };
		var cmdB = new CreateOrderCommand { ProductId = 1, Quantity = 9, CustomerId = 1 };

		var first = await admission.AdmitAsync(
			CapabilityIds.OrdersCreate,
			key,
			CapabilityAdmissionService.SerializeCreateOrderIntent(cmdA),
			CapabilityAdmissionService.HashCreateOrderIntent(cmdA),
			CancellationToken.None);
		first.Should().BeOfType<AdmissionDecision.Defer>();

		var conflict = await admission.AdmitAsync(
			CapabilityIds.OrdersCreate,
			key,
			CapabilityAdmissionService.SerializeCreateOrderIntent(cmdB),
			CapabilityAdmissionService.HashCreateOrderIntent(cmdB),
			CancellationToken.None);

		var deny = conflict.Should().BeOfType<AdmissionDecision.Deny>().Subject;
		deny.StatusCode.Should().Be(422);

		_output.WriteLine(
			"Identity semantics: ticket dedup key = CapabilityId + RequestKey; " +
			"IntentHash must match while Pending. HTTP fingerprint-off / MCP memory store may " +
			"replay the first Pending response without re-entering Admit.");
	}

	[Fact]
	public async Task Human_release_executes_existing_handler_once_with_outbox()
	{
		await _fixture.ResetLabObservationAsync();
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();

		var create = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 2);
		var pending = ParsePending(create.Body);
		CreateOrderMediatorCount(create.Spans).Should().Be(0);

		capture.Clear();
		using var releaseRequest = new HttpRequestMessage(
			HttpMethod.Post,
			$"/api/v1/admission/tickets/{pending.TicketId}/release");
		releaseRequest.Headers.TryAddWithoutValidation("X-Released-By", "lab-human");
		using var releaseResponse = await _http.SendAsync(releaseRequest);
		var releaseBody = await releaseResponse.Content.ReadAsStringAsync();
		_output.WriteLine(releaseBody);

		releaseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		using var doc = JsonDocument.Parse(releaseBody);
		var orderId = doc.RootElement.GetProperty("orderId").GetGuid();
		orderId.Should().NotBeEmpty();

		capture.All.Count(IsMediator).Should().BeGreaterThan(0, "release must Send CreateOrderCommand");
		var outbox = await OrderOutboxObserver.WaitUntilExistsAsync(_deferHost.Services, orderId);
		outbox.OrderId.Should().Be(orderId);

		await Wait.UntilAsync(
			() => _fixture.ProcessedEvents.Any(e => e.OrderId == orderId),
			TimeSpan.FromSeconds(20));

		(await GetTicketAsync(pending.TicketId))!.Status.Should().Be("Released");
	}

	[Fact]
	public async Task Concurrent_release_only_one_winner_one_order()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var create = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 1);
		var ticketId = ParsePending(create.Body).TicketId;

		async Task<(HttpStatusCode Status, string Body)> ReleaseOnce()
		{
			using var req = new HttpRequestMessage(
				HttpMethod.Post,
				$"/api/v1/admission/tickets/{ticketId}/release");
			using var res = await _http.SendAsync(req);
			return (res.StatusCode, await res.Content.ReadAsStringAsync());
		}

		var results = await Task.WhenAll(ReleaseOnce(), ReleaseOnce(), ReleaseOnce());
		var wins = results.Where(r => r.Status == HttpStatusCode.OK).ToList();
		var conflicts = results.Where(r => r.Status == HttpStatusCode.Conflict).ToList();

		wins.Should().HaveCount(1, "atomic Pending→Released claim admits one execution");
		conflicts.Should().HaveCount(2);

		using var doc = JsonDocument.Parse(wins[0].Body);
		var orderId = doc.RootElement.GetProperty("orderId").GetGuid();
		(await OrderOutboxObserver.FindByOrderIdAsync(_deferHost.Services, orderId))
			.Should().HaveCount(1);
	}

	[Fact]
	public async Task Replay_after_release_does_not_create_second_order_via_admit()
	{
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var create = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 1);
		var ticketId = ParsePending(create.Body).TicketId;

		using var releaseReq = new HttpRequestMessage(
			HttpMethod.Post,
			$"/api/v1/admission/tickets/{ticketId}/release");
		using var releaseRes = await _http.SendAsync(releaseReq);
		releaseRes.StatusCode.Should().Be(HttpStatusCode.OK);
		var orderId = (await releaseRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();

		var outboxBefore = (await OrderOutboxObserver.FindByOrderIdAsync(_deferHost.Services, orderId)).Count;

		var replay = await HttpOrderCreate.PostAsync(_http, capture, key, quantity: 1);
		replay.HttpStatus.Should().Be((int)HttpStatusCode.Accepted);
		replay.CachedResponseHeader.Should().BeTrue("surface idempotency replays Pending envelope");
		ParsePending(replay.Body).TicketId.Should().Be(ticketId);

		(await OrderOutboxObserver.FindByOrderIdAsync(_deferHost.Services, orderId))
			.Should().HaveCount(outboxBefore);

		await using var scope = _deferHost.Services.CreateAsyncScope();
		var admission = scope.ServiceProvider.GetRequiredService<ICapabilityAdmission>();
		var cmd = new CreateOrderCommand { ProductId = 1, Quantity = 1, CustomerId = 1 };
		var decision = await admission.AdmitAsync(
			CapabilityIds.OrdersCreate,
			key,
			CapabilityAdmissionService.SerializeCreateOrderIntent(cmd),
			CapabilityAdmissionService.HashCreateOrderIntent(cmd),
			CancellationToken.None);
		decision.Should().BeOfType<AdmissionDecision.Deny>();
	}

	[Fact]
	public async Task Expired_ticket_cannot_be_released()
	{
		var shortTtlHost = _fixture.WithWebHostBuilder(builder =>
		{
			builder.ConfigureTestServices(services =>
			{
				services.PostConfigure<CapabilityAdmissionOptions>(o =>
				{
					o.DeferredCapabilities.Clear();
					o.DeferredCapabilities.Add(CapabilityIds.OrdersCreate);
					o.TicketTtl = TimeSpan.FromMilliseconds(30);
				});
			});
		});
		var http = shortTtlHost.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});

		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var create = await HttpOrderCreate.PostAsync(http, capture, key, quantity: 1);
		var ticketId = ParsePending(create.Body).TicketId;
		var outboxBefore = await CountOrderCreatedOutboxAsync(shortTtlHost.Services);

		await Task.Delay(80);

		using var releaseReq = new HttpRequestMessage(
			HttpMethod.Post,
			$"/api/v1/admission/tickets/{ticketId}/release");
		using var releaseRes = await http.SendAsync(releaseReq);
		releaseRes.StatusCode.Should().Be(HttpStatusCode.Gone);

		(await CountOrderCreatedOutboxAsync(shortTtlHost.Services)).Should().Be(outboxBefore);
		await using var scope = shortTtlHost.Services.CreateAsyncScope();
		var ticket = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
			.IntentTickets.AsNoTracking().FirstAsync(t => t.Id == ticketId);
		ticket.ExecutionOrderId.Should().BeNull();
		ticket.Status.Should().BeOneOf(IntentTicketStatus.Expired, IntentTicketStatus.Pending);
	}

	[Fact]
	public async Task Control_without_defer_policy_allows_existing_send()
	{
		var http = _fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
		using var capture = new InProcessActivityCapture();
		var key = Ulid.NewUlid().ToString();
		var result = await HttpOrderCreate.PostAsync(http, capture, key, quantity: 1);

		result.HttpStatus.Should().Be((int)HttpStatusCode.OK);
		result.OrderId.Should().NotBeEmpty();
		CreateOrderMediatorCount(result.Spans).Should().BeGreaterThan(0);
	}

	private async Task<int> CountOrderCreatedOutboxAsync(IServiceProvider? services = null)
	{
		await using var scope = (services ?? _deferHost.Services).CreateAsyncScope();
		var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
		return await db.OutboxMessages.CountAsync(m =>
			m.EventType == nameof(OrderCreatedIntegrationEvent));
	}

	private async Task<TicketView?> GetTicketAsync(Guid ticketId)
	{
		using var res = await _http.GetAsync($"/api/v1/admission/tickets/{ticketId}");
		if (res.StatusCode == HttpStatusCode.NotFound)
			return null;
		res.EnsureSuccessStatusCode();
		return await res.Content.ReadFromJsonAsync<TicketView>(JsonOptions);
	}

	private static int CreateOrderMediatorCount(IReadOnlyList<CapturedActivity> spans) =>
		spans.Count(s => IsMediator(s)
			&& s.DisplayName.Contains("CreateOrderCommand", StringComparison.Ordinal));

	private static AdmissionPendingResponse ParsePending(string body)
	{
		var pending = JsonSerializer.Deserialize<AdmissionPendingResponse>(body, JsonOptions);
		pending.Should().NotBeNull();
		return pending!;
	}

	private static AdmissionPendingResponse ParsePendingFromMcp(ModelContextProtocol.Protocol.CallToolResult result)
	{
		result.StructuredContent.Should().NotBeNull();
		var structured = result.StructuredContent!.Value;
		if (structured.ValueKind == JsonValueKind.Object
		    && structured.TryGetProperty("value", out var value))
		{
			return JsonSerializer.Deserialize<AdmissionPendingResponse>(value.GetRawText(), JsonOptions)!;
		}

		return JsonSerializer.Deserialize<AdmissionPendingResponse>(structured.GetRawText(), JsonOptions)!;
	}

	private sealed record TicketView(
		Guid TicketId,
		string CapabilityId,
		string Status,
		DateTimeOffset CreatedAt,
		DateTimeOffset ExpiresAt,
		DateTimeOffset? ReleasedAt,
		string? ReleasedBy,
		Guid? ExecutionOrderId);
}
