using System.Text.Json;
using BuildingBlocks.Mcp;
using FluentAssertions;
using IntegrationTests.Aspire;
using IntegrationTests.Infrastructure.Mcp;
using IntegrationTests.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit.Abstractions;
using static IntegrationTests.Infrastructure.Telemetry.LabTrace;

namespace IntegrationTests.Experiments.McpDistributedIdempotency;

/// <summary>
/// Lab prototype (not a numbered Exp 21): FeatureFusion <c>orders.create</c> with distributed
/// MCP idempotency (IDistributedCache payloads + MCP <see cref="RedisMcpIdempotencyLock"/>).
/// Default Program.cs remains <c>UseMemoryIdempotency</c>. This host is WithWebHostBuilder-only.
/// Hypothesis: wait-and-replay holds across two WAF instances sharing Aspire Redis while the lease
/// is valid; lease expiry can overlap (not exactly-once); MRTR unconfirmed does not write the store.
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class McpDistributedIdempotencyExperimentTests : IDisposable
{
	private const string ToolName = "orders.create";
	private const int ProductId = 1;
	private const int CustomerId = 1;
	private const int Quantity = 2;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AspireFixture _fixture;
	private readonly WebApplicationFactory<Program> _factoryA;
	private readonly WebApplicationFactory<Program> _factoryB;
	private readonly HttpClient _httpA;
	private readonly HttpClient _httpB;
	private readonly ITestOutputHelper _output;

	public McpDistributedIdempotencyExperimentTests(AspireFixture fixture, ITestOutputHelper output)
	{
		_fixture = fixture;
		_output = output;
		_factoryA = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureDistributed));
		_factoryB = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(ConfigureDistributed));
		_httpA = _factoryA.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
		_httpB = _factoryB.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
	}

	/// <summary>Miss on factory A, confirmed replay on factory B returns the same order id without a second create.</summary>
	[Fact]
	public async Task Cross_factory_replay_returns_same_order()
	{
		await _fixture.ResetLabObservationAsync();
		using var capture = new InProcessActivityCapture();
		var key = System.Ulid.NewUlid().ToString();
		await using var mcpA = await LabMcpClient.CreateJuly2026Async(_httpA);
		await using var mcpB = await LabMcpClient.CreateJuly2026Async(_httpB);
		var before = capture.All.Count(IsMediator);

		var first = await mcpA.CallToolAsync(ToolName, OrderArgs(key, confirmed: true));
		(first.IsError ?? false).Should().BeFalse(McpToolResults.GetText(first));
		var order = McpToolResults.TryParseOrder(first, JsonOptions);
		order.Should().NotBeNull();
		var afterFirst = capture.All.Count(IsMediator);
		afterFirst.Should().BeGreaterThan(before);

		var replay = await mcpB.CallToolAsync(ToolName, OrderArgs(key, confirmed: true));
		(replay.IsError ?? false).Should().BeFalse(McpToolResults.GetText(replay));
		var replayed = McpToolResults.TryParseOrder(replay, JsonOptions);
		replayed.Should().NotBeNull();
		replayed!.OrderId.Should().Be(order!.OrderId);
		capture.All.Count(IsMediator).Should().Be(afterFirst);
		_output.WriteLine(McpToolResults.GetText(replay));
	}

	/// <summary>Concurrent confirmed writes with one key across two HTTP clients converge to one order while the lease is valid.</summary>
	[Fact]
	public async Task Concurrent_same_key_across_factories_is_one_order_while_lease_valid()
	{
		await _fixture.ResetLabObservationAsync();
		var key = System.Ulid.NewUlid().ToString();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var tasks = Enumerable.Range(0, 3).Select(async i =>
		{
			await gate.Task;
			var http = i % 2 == 0 ? _httpA : _httpB;
			await using var mcp = await LabMcpClient.CreateJuly2026Async(http);
			return await mcp.CallToolAsync(ToolName, OrderArgs(key, confirmed: true));
		}).ToArray();
		gate.SetResult();
		var results = await Task.WhenAll(tasks);

		results.Should().OnlyContain(r => !(r.IsError ?? false));
		var ids = results
			.Select(r => McpToolResults.TryParseOrder(r, JsonOptions)?.OrderId)
			.Where(id => id is { } g && g != Guid.Empty)
			.Distinct()
			.ToList();
		ids.Should().HaveCount(1);
	}

	/// <summary>
	/// 2026 unconfirmed elicitation/decline must not persist an idempotency payload;
	/// a later confirmed call on the other factory may execute (no exactly-once before accept).
	/// Accepted create then cross-factory confirmed replay shares the order id.
	/// </summary>
	[Fact]
	public async Task Mrtr_unconfirmed_does_not_write_store_accept_then_replays_across_factory()
	{
		await _fixture.ResetLabObservationAsync();
		var declineKey = System.Ulid.NewUlid().ToString();
		ElicitRequestParams? elicitation = null;
		await using var declining = await LabMcpClient.CreateJuly2026Async(_httpA, Decline(req => elicitation = req));
		var declined = await declining.CallToolAsync(ToolName, OrderArgs(declineKey, confirmed: false));
		elicitation.Should().NotBeNull();
		(declined.IsError ?? false).Should().BeTrue();

		await using var other = await LabMcpClient.CreateJuly2026Async(_httpB);
		var afterDecline = await other.CallToolAsync(ToolName, OrderArgs(declineKey, confirmed: true));
		(afterDecline.IsError ?? false).Should().BeFalse(McpToolResults.GetText(afterDecline));
		McpToolResults.TryParseOrder(afterDecline, JsonOptions).Should().NotBeNull();

		var acceptKey = System.Ulid.NewUlid().ToString();
		await using var accepting = await LabMcpClient.CreateJuly2026Async(_httpA, Accept());
		var created = await accepting.CallToolAsync(ToolName, OrderArgs(acceptKey, confirmed: false));
		(created.IsError ?? false).Should().BeFalse(McpToolResults.GetText(created));
		var order = McpToolResults.TryParseOrder(created, JsonOptions);
		order.Should().NotBeNull();

		var replay = await other.CallToolAsync(ToolName, OrderArgs(acceptKey, confirmed: true));
		McpToolResults.TryParseOrder(replay, JsonOptions)!.OrderId.Should().Be(order!.OrderId);
	}

	public void Dispose()
	{
		_httpA.Dispose();
		_httpB.Dispose();
		_factoryA.Dispose();
		_factoryB.Dispose();
	}

	private static void ConfigureDistributed(IServiceCollection services)
	{
		services.RemoveAll<IMcpIdempotencyStore>();
		services.AddSingleton(new McpIdempotencyOptions
		{
			Lease = TimeSpan.FromMinutes(2),
			PayloadTtl = TimeSpan.FromHours(1),
			AcquireWaitBudget = TimeSpan.FromSeconds(30),
			PollDelay = TimeSpan.FromMilliseconds(20)
		});
		services.AddSingleton<IMcpIdempotencyStore>(sp =>
			new DistributedCacheIdempotencyStore(
				sp.GetRequiredService<IDistributedCache>(),
				TimeSpan.FromHours(1)));
		services.AddSingleton<IMcpIdempotencyLock>(sp =>
			new RedisMcpIdempotencyLock(sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>()));
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

	private static McpClientHandlers Accept()
		=> new()
		{
			ElicitationHandler = (_, _) => ValueTask.FromResult(new ElicitResult
			{
				Action = "accept",
				Content = new Dictionary<string, JsonElement>
				{
					[McpDefaults.ConfirmedArgument] = JsonSerializer.SerializeToElement(true)
				}
			})
		};

	private static McpClientHandlers Decline(Action<ElicitRequestParams>? onElicit)
		=> new()
		{
			ElicitationHandler = (request, _) =>
			{
				if (request is not null)
					onElicit?.Invoke(request);
				return ValueTask.FromResult(new ElicitResult { Action = "decline" });
			}
		};
}
