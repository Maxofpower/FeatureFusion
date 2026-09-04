using System.Text.Json;
using BuildingBlocks.Mcp;
using BuildingBlocks.Mcp.Catalog;
using BuildingBlocks.Mcp.Hosting;
using BuildingBlocks.Mcp.Invocation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Mcp.Tests;

public sealed class CatalogAndInvokerTests
{
	[Fact]
	public void Scan_Ignores_Unmarked_And_Throws_On_Duplicate_Names()
	{
		var scanned = McpToolScanner.Scan(typeof(ListedOrder).Assembly);
		Assert.Contains(scanned, t => t.Name == "tests.list");
		Assert.DoesNotContain(scanned, t => t.MessageType == typeof(UnmarkedMessage));
		Assert.Throws<McpCatalogException>(() => McpToolScanner.EnsureUniqueNames(
		[
			scanned.First(t => t.Name == "tests.list"),
			scanned.First(t => t.Name == "tests.list")
		]));
	}

	[Fact]
	public void Schema_Optional_Defaults_Enums_And_Descriptions()
	{
		var d = McpToolScanner.FromType(typeof(SchemaProbe), null);

		var requiredName = Assert.Single(d.Properties, p => p.Name == "requiredName");
		Assert.True(requiredName.Required);

		var cursor = Assert.Single(d.Properties, p => p.Name == "cursor");
		Assert.False(cursor.Required);
		Assert.Equal("Pagination cursor", cursor.Description);

		var qty = Assert.Single(d.Properties, p => p.Name == "qty");
		Assert.True(qty.Required);

		var limit = Assert.Single(d.Properties, p => p.Name == "limit");
		Assert.False(limit.Required);

		var named = Assert.Single(d.Properties, p => p.Name == "named");
		Assert.False(named.Required);
		Assert.Equal("string", named.JsonType);
		Assert.Equal(["Ascending", "Descending"], named.EnumNames);

		var numeric = Assert.Single(d.Properties, p => p.Name == "numeric");
		Assert.False(numeric.Required);
		Assert.Equal("integer", numeric.JsonType);
		Assert.Equal([0L, 1L], numeric.EnumValues);
	}

	[Fact]
	public void Query_Descriptor_Is_Not_Write_Idempotent()
	{
		var d = McpToolScanner.FromType(typeof(ListedOrder), null);
		Assert.Equal(McpToolKind.Query, d.Kind);
		Assert.False(d.Idempotent);
	}

	[Fact]
	public async Task Command_Without_Idempotent_Flag_Still_Requires_Key()
	{
		var store = new MemoryIdempotencyStore();
		var invoker = CreateInvoker(
			typeof(CreateListedOrderDefault),
			(_, _, _, _) => Task.FromResult(McpResult.Ok<object?>(1)),
			store);
		var missing = await invoker.InvokeAsync(
			"tests.create-default",
			JsonDocument.Parse("""{"qty":1}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.IdempotencyKeyRequired, missing.Error!.Code);
	}

	[Fact]
	public async Task Query_Ignores_Idempotency_Store()
	{
		var calls = 0;
		var store = new MemoryIdempotencyStore();
		var invoker = CreateInvoker(
			typeof(QueryMarkedIdempotent),
			(_, _, _, _) =>
			{
				calls++;
				return Task.FromResult(McpResult.Ok<object?>(1));
			},
			store);
		var args = JsonDocument.Parse("""{"sku":"x","idempotencyKey":"k"}""").RootElement;
		await invoker.InvokeAsync("tests.query-idemp", args, McpInvokeContext.None, CancellationToken.None);
		await invoker.InvokeAsync("tests.query-idemp", args, McpInvokeContext.None, CancellationToken.None);
		Assert.Equal(2, calls);
	}

	[Fact]
	public async Task UseMemoryIdempotency_Registers_Store()
	{
		var services = new ServiceCollection();
		services.AddBuildingBlocksMcp(o =>
		{
			o.UseMemoryIdempotency(TimeSpan.FromHours(1));
			o.MapTool<ProbeMsg, string>(
				"tests.mem-idemp",
				"Mem",
				(_, _, _) => Task.FromResult(McpResult.Ok("ok")),
				a => a.Kind = McpToolKind.Query);
		});
		await using var sp = services.BuildServiceProvider();
		Assert.IsType<MemoryIdempotencyStore>(sp.GetRequiredService<IMcpIdempotencyStore>());
	}

	[Fact]
	public async Task WithMcp_And_Scan_Dedupe_Same_Name()
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.Services.AddBuildingBlocksMcp(o => o.ScanAssemblyContaining<EndpointPingRequest>());
		await using var app = builder.Build();
		app.MapGet("/ping", EndpointPingHost.Ping).WithMcp(app);
		var invoker = app.Services.GetRequiredService<IMcpInvoker>();
		Assert.Equal(1, invoker.Catalog.Count(t => t.Name == "tests.endpoint-ping"));
		var result = await invoker.InvokeAsync(
			"tests.endpoint-ping",
			JsonDocument.Parse("""{"name":"Ada"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.True(result.IsSuccess);
		Assert.Equal("pong:Ada", result.Value);
	}

	[Fact]
	public async Task WithMcp_Named_Infers_Query_From_Get()
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.Services.AddBuildingBlocksMcp(o => { });
		await using var app = builder.Build();
		app.MapGet("/named-ping", WithMcpNamedPing).WithMcp(app, "tests.withmcp-named", "Named ping");
		var invoker = app.Services.GetRequiredService<IMcpInvoker>();
		var tool = Assert.Single(invoker.Catalog, t => t.Name == "tests.withmcp-named");
		Assert.Equal(McpToolKind.Query, tool.Kind);
		Assert.False(tool.Idempotent);
	}

	public static string WithMcpNamedPing([AsParameters] EndpointPingRequest request)
		=> $"pong:{request.Name}";

	[Fact]
	public async Task Invoke_RoundTrip_And_Deny_Unknown()
	{
		var invoker = CreateInvoker(handler: (_, msg, _, _) =>
		{
			var m = (ListedOrder)msg;
			return Task.FromResult(McpResult.Ok<object?>(new { m.Sku }));
		});

		var json = JsonDocument.Parse("""{"sku":"A-1"}""").RootElement;
		var ok = await invoker.InvokeAsync("tests.list", json, McpInvokeContext.None, CancellationToken.None);
		Assert.True(ok.IsSuccess);

		var missing = await invoker.InvokeAsync("nope", json, McpInvokeContext.None, CancellationToken.None);
		Assert.False(missing.IsSuccess);
		Assert.Equal(McpErrorCode.NotFound, missing.Error!.Code);
	}

	[Fact]
	public async Task Idempotent_Write_Requires_Key_And_Does_Not_Double_Dispatch()
	{
		var calls = 0;
		var store = new MemoryIdempotencyStore();
		var invoker = CreateInvoker(
			typeof(CreateListedOrder),
			(_, _, _, _) =>
			{
				calls++;
				return Task.FromResult(McpResult.Ok<object?>(new { Id = 7 }));
			},
			store);

		var missing = await invoker.InvokeAsync(
			"tests.create",
			JsonDocument.Parse("""{"qty":1}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.IdempotencyKeyRequired, missing.Error!.Code);
		Assert.Equal(0, calls);

		var args = JsonDocument.Parse("""{"qty":1,"idempotencyKey":"k1"}""").RootElement;
		var first = await invoker.InvokeAsync("tests.create", args, McpInvokeContext.None, CancellationToken.None);
		var second = await invoker.InvokeAsync("tests.create", args, McpInvokeContext.None, CancellationToken.None);
		Assert.True(first.IsSuccess);
		Assert.True(second.IsSuccess);
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task Confirmation_Timeout_And_Invalid_Args()
	{
		var invoker = CreateInvoker(
			typeof(ConfirmOrder),
			async (_, _, _, ct) =>
			{
				await Task.Delay(500, ct);
				return McpResult.Ok<object?>(1);
			});

		var unconfirmed = await invoker.InvokeAsync(
			"tests.confirm",
			JsonDocument.Parse("{}").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.ConfirmationRequired, unconfirmed.Error!.Code);

		var timed = await invoker.InvokeAsync(
			"tests.confirm",
			JsonDocument.Parse("""{"confirmed":true}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.Timeout, timed.Error!.Code);

		var listInvoker = CreateInvoker((_, _, _, _) => Task.FromResult(McpResult.Ok<object?>(1)));
		var bad = await listInvoker.InvokeAsync(
			"tests.list",
			JsonDocument.Parse("[]").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.Validation, bad.Error!.Code);
	}

	[Fact]
	public async Task Filter_Hides_Tool_From_List_And_Invoke()
	{
		var invoker = CreateInvoker(
			typeof(ListedOrder),
			(_, _, _, _) => Task.FromResult(McpResult.Ok<object?>(1)),
			filters: [new HideAllFilter()]);

		var list = await invoker.ListVisibleAsync(McpInvokeContext.None, CancellationToken.None);
		Assert.Empty(list);
		var call = await invoker.InvokeAsync(
			"tests.list",
			JsonDocument.Parse("""{"sku":"x"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.Forbidden, call.Error!.Code);
	}

	[Fact]
	public async Task Handler_Throw_Is_Internal_Without_Stack()
	{
		var invoker = CreateInvoker((_, _, _, _) => throw new InvalidOperationException("secret-stack"));
		var result = await invoker.InvokeAsync(
			"tests.list",
			JsonDocument.Parse("""{"sku":"x"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.Internal, result.Error!.Code);
		Assert.DoesNotContain("secret-stack", result.Error.Message);
	}

	[Fact]
	public async Task Handler_Throw_Includes_Exception_Message_When_Details_Enabled()
	{
		const string knownMessage = "known-exception-detail";
		var descriptor = McpToolScanner.FromType(
			typeof(ListedOrder),
			(_, _, _, _) => throw new InvalidOperationException(knownMessage));
		var invoker = new McpInvoker(
			[descriptor],
			new ServiceCollection().BuildServiceProvider(),
			[],
			new NoOpRateLimiter(),
			new DefaultMcpResultMapper(),
			dispatcher: null,
			idempotency: null,
			resilience: null,
			telemetry: null,
			includeExceptionDetails: true);

		var result = await invoker.InvokeAsync(
			"tests.list",
			JsonDocument.Parse("""{"sku":"x"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);

		Assert.Equal(McpErrorCode.Internal, result.Error!.Code);
		Assert.Contains(knownMessage, result.Error.Message);
	}

	[Fact]
	public async Task Writes_Are_Not_Retried_By_Invoker()
	{
		var calls = 0;
		var invoker = CreateInvoker(
			typeof(CreateListedOrder),
			(_, _, _, _) =>
			{
				calls++;
				throw new InvalidOperationException("fail");
			});

		await invoker.InvokeAsync(
			"tests.create",
			JsonDocument.Parse("""{"qty":1,"idempotencyKey":"a"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(1, calls);
	}

	[Fact]
	public void McpPage_Has_Items_And_Cursor()
	{
		var page = McpPage<int>.From([1, 2], "c2");
		var json = JsonSerializer.Serialize(page, McpJson.Options);
		Assert.Contains("items", json);
		Assert.Contains("nextCursor", json);
	}

	[Fact]
	public async Task DryRun_Is_On_Context_And_Accessor_During_Invoke()
	{
		McpInvokeContext? seen = null;
		var services = new ServiceCollection();
		services.AddBuildingBlocksMcp(o =>
		{
			o.MapTool<DryRunMsg, bool>(
				"tests.dry",
				"Dry",
				(msg, ctx, _) =>
				{
					seen = ctx;
					return Task.FromResult(McpResult.Ok(ctx.DryRun));
				},
				a =>
				{
					a.Kind = McpToolKind.Query;
					a.AllowDryRun = true;
				});
		});

		await using var sp = services.BuildServiceProvider();
		var invoker = sp.GetRequiredService<IMcpInvoker>();
		var accessor = sp.GetRequiredService<IMcpInvokeContextAccessor>();
		Assert.Null(accessor.Current);

		var result = await invoker.InvokeAsync(
			"tests.dry",
			JsonDocument.Parse("""{"dryRun":true}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);

		Assert.True(result.IsSuccess);
		Assert.Equal(true, result.Value);
		Assert.True(seen!.DryRun);
		Assert.Null(accessor.Current);
	}

	[Fact]
	public void DuckTyped_Result_Maps_Failure()
	{
		var mapper = new DefaultMcpResultMapper();
		var mapped = mapper.Map(new FakeResult { IsSuccess = false, Error = "bad", Value = 0, StatusCode = 400 });
		Assert.False(mapped.IsSuccess);
		Assert.Equal(McpErrorCode.Validation, mapped.Error!.Code);
	}

	private static IMcpInvoker CreateInvoker(
		Func<IServiceProvider, object, McpInvokeContext, CancellationToken, Task<McpResult<object?>>>? handler = null,
		IMcpIdempotencyStore? store = null,
		IMcpToolFilter[]? filters = null)
		=> CreateInvoker(typeof(ListedOrder), handler!, store, filters);

	private static IMcpInvoker CreateInvoker(
		Type messageType,
		Func<IServiceProvider, object, McpInvokeContext, CancellationToken, Task<McpResult<object?>>> handler,
		IMcpIdempotencyStore? store = null,
		IMcpToolFilter[]? filters = null)
	{
		var d = McpToolScanner.FromType(messageType, handler);
		var services = new ServiceCollection().BuildServiceProvider();
		return new McpInvoker(
			[d],
			services,
			filters ?? [],
			new NoOpRateLimiter(),
			new DefaultMcpResultMapper(),
			dispatcher: null,
			store,
			resilience: null,
			telemetry: null,
			includeExceptionDetails: false);
	}

	[Fact]
	public async Task MapTool_Scoped_Service_Resolves_When_ValidateScopes()
	{
		var services = new ServiceCollection();
		services.AddScoped<ScopedProbe>();
		services.AddBuildingBlocksMcp(o =>
		{
			o.MapTool<ProbeMsg, string>(
				"tests.scoped",
				"Scoped",
				async (sp, _, _, _) =>
				{
					var probe = sp.GetRequiredService<ScopedProbe>();
					return McpResult.Ok(probe.Marker);
				},
				a => a.Kind = McpToolKind.Query);
		});

		await using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		var invoker = sp.GetRequiredService<IMcpInvoker>();
		var result = await invoker.InvokeAsync(
			"tests.scoped",
			JsonDocument.Parse("{}").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.True(result.IsSuccess);
		Assert.Equal("ok", result.Value);
	}

	[Fact]
	public async Task RateLimiter_Deny_Is_RateLimited()
	{
		var d = McpToolScanner.FromType(typeof(ListedOrder), (_, _, _, _) => Task.FromResult(McpResult.Ok<object?>(1)));
		var invoker = new McpInvoker(
			[d],
			new ServiceCollection().BuildServiceProvider(),
			[],
			new DenyRateLimiter(),
			new DefaultMcpResultMapper(),
			dispatcher: null,
			idempotency: null,
			resilience: null,
			telemetry: null,
			includeExceptionDetails: false);
		var result = await invoker.InvokeAsync(
			"tests.list",
			JsonDocument.Parse("""{"sku":"x"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.Equal(McpErrorCode.RateLimited, result.Error!.Code);
		Assert.Equal(7, result.Error.RetryAfterSeconds);
	}

	[Fact]
	public async Task Idempotency_Keys_Are_Namespaced_Per_Tool()
	{
		var callsA = 0;
		var callsB = 0;
		var store = new MemoryIdempotencyStore();
		var a = McpToolScanner.FromType(typeof(CreateListedOrder), (_, _, _, _) =>
		{
			callsA++;
			return Task.FromResult(McpResult.Ok<object?>(new { Id = 1 }));
		});
		var b = McpToolScanner.FromType(typeof(CreateListedOrderB), (_, _, _, _) =>
		{
			callsB++;
			return Task.FromResult(McpResult.Ok<object?>(new { Id = 2 }));
		});
		var invoker = new McpInvoker(
			[a, b],
			new ServiceCollection().BuildServiceProvider(),
			[],
			new NoOpRateLimiter(),
			new DefaultMcpResultMapper(),
			null,
			store,
			null,
			null,
			false);
		var args = JsonDocument.Parse("""{"qty":1,"idempotencyKey":"same"}""").RootElement;
		await invoker.InvokeAsync("tests.create", args, McpInvokeContext.None, CancellationToken.None);
		await invoker.InvokeAsync("tests.create-b", args, McpInvokeContext.None, CancellationToken.None);
		Assert.Equal(1, callsA);
		Assert.Equal(1, callsB);
	}

	[Fact]
	public async Task Idempotency_Replay_Returns_JsonElement()
	{
		var store = new MemoryIdempotencyStore();
		var invoker = CreateInvoker(
			typeof(CreateListedOrder),
			(_, _, _, _) => Task.FromResult(McpResult.Ok<object?>(new { Id = 7 })),
			store);
		var args = JsonDocument.Parse("""{"qty":1,"idempotencyKey":"k-json"}""").RootElement;
		await invoker.InvokeAsync("tests.create", args, McpInvokeContext.None, CancellationToken.None);
		var second = await invoker.InvokeAsync("tests.create", args, McpInvokeContext.None, CancellationToken.None);
		Assert.True(second.IsSuccess);
		Assert.IsType<JsonElement>(second.Value);
		Assert.Contains("7", ((JsonElement)second.Value!).GetRawText(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Idempotency_Store_Honors_Ttl()
	{
		var store = new MemoryIdempotencyStore(TimeSpan.FromMilliseconds(40));
		await store.SetAsync("k", """{"n":1}""", CancellationToken.None);
		Assert.NotNull(await store.GetAsync("k", CancellationToken.None));
		await Task.Delay(80);
		Assert.Null(await store.GetAsync("k", CancellationToken.None));
	}

	[Fact]
	public async Task Idempotency_Lock_Single_Dispatch_Under_Concurrency()
	{
		var calls = 0;
		var store = new MemoryIdempotencyStore();
		var invoker = CreateInvoker(
			typeof(CreateListedOrder),
			async (_, _, _, _) =>
			{
				await Task.Delay(80);
				Interlocked.Increment(ref calls);
				return McpResult.Ok<object?>(new { Id = 1 });
			},
			store);
		var args = JsonDocument.Parse("""{"qty":1,"idempotencyKey":"parallel"}""").RootElement;
		await Task.WhenAll(
			invoker.InvokeAsync("tests.create", args, McpInvokeContext.None, CancellationToken.None),
			invoker.InvokeAsync("tests.create", args, McpInvokeContext.None, CancellationToken.None));
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task Scan_Public_Static_Method_Is_A_Tool()
	{
		var services = new ServiceCollection();
		services.AddBuildingBlocksMcp(o => o.ScanAssemblyContaining<EndpointPingRequest>());
		await using var sp = services.BuildServiceProvider();
		var invoker = sp.GetRequiredService<IMcpInvoker>();
		Assert.Contains(invoker.Catalog, t => t.Name == "tests.endpoint-ping");
		var result = await invoker.InvokeAsync(
			"tests.endpoint-ping",
			JsonDocument.Parse("""{"name":"Ada"}""").RootElement,
			McpInvokeContext.None,
			CancellationToken.None);
		Assert.True(result.IsSuccess);
		Assert.Equal("pong:Ada", result.Value);
	}

	private sealed class DenyRateLimiter : IMcpRateLimiter
	{
		public ValueTask<McpRateLimitDecision> TryAcquireAsync(string toolName, McpInvokeContext context, CancellationToken cancellationToken)
			=> ValueTask.FromResult(McpRateLimitDecision.Deny(7));
	}

	private sealed class ScopedProbe
	{
		public string Marker { get; } = "ok";
	}

	private sealed class ProbeMsg
	{
	}

	private sealed class HideAllFilter : IMcpToolFilter
	{
		public ValueTask<bool> IsVisibleAsync(McpToolDescriptor tool, McpInvokeContext context, CancellationToken cancellationToken)
			=> ValueTask.FromResult(false);
	}

	public sealed class FakeResult
	{
		public bool IsSuccess { get; set; }
		public string Error { get; set; } = "";
		public int Value { get; set; }
		public int StatusCode { get; set; }
	}
}

[McpTool("tests.list", Description = "List", Kind = McpToolKind.Query)]
public sealed class ListedOrder
{
	public string Sku { get; set; } = "";
}

[McpTool("tests.create", Description = "Create", Kind = McpToolKind.Command, Idempotent = true)]
public sealed class CreateListedOrder
{
	public int Qty { get; set; }
}

[McpTool("tests.create-b", Description = "Create B", Kind = McpToolKind.Command, Idempotent = true)]
public sealed class CreateListedOrderB
{
	public int Qty { get; set; }
}

public static class EndpointPingHost
{
	[McpTool("tests.endpoint-ping", Description = "Ping", Kind = McpToolKind.Query)]
	public static string Ping([AsParameters] EndpointPingRequest request)
		=> $"pong:{request.Name}";
}

public sealed class EndpointPingRequest
{
	public string Name { get; set; } = "";
}

[McpTool("tests.create-default", Description = "Create default", Kind = McpToolKind.Command)]
public sealed class CreateListedOrderDefault
{
	public int Qty { get; set; }
}

[McpTool("tests.query-idemp", Description = "Query", Kind = McpToolKind.Query, Idempotent = true)]
public sealed class QueryMarkedIdempotent
{
	public string Sku { get; set; } = "";
}

[McpTool("tests.confirm", Description = "Confirm", Kind = McpToolKind.Command, Idempotent = false, RequireConfirmation = true, TimeoutMilliseconds = 20)]
public sealed class ConfirmOrder
{
}

public sealed class UnmarkedMessage
{
	public int Id { get; set; }
}

public sealed class DryRunMsg
{
}

[McpTool("tests.schema", Description = "Schema probe", Kind = McpToolKind.Query)]
public sealed class SchemaProbe
{
	public string RequiredName { get; set; } = default!;

	[System.ComponentModel.Description("Pagination cursor")]
	public string Cursor { get; set; } = "";

	public int Qty { get; set; }

	public int Limit { get; set; } = 20;

	public NamedKind Named { get; set; }

	public NumericKind Numeric { get; set; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum NamedKind
{
	Ascending,
	Descending
}

public enum NumericKind
{
	Zero,
	One
}
