# BuildingBlocks.Mcp

Message types, public static endpoint methods, or `MapTool` handlers become MCP tools. The official C# SDK owns the protocol. This package owns the catalog, `McpResult`, filters, and safe defaults.

See ADR [0002](../adr/0002-mcp-message-tools.md) and the [package README](../../src/BuildingBlocks/Mcp/PACKAGE_README.md).

## Install

```bash
dotnet add package BuildingBlocks.Mcp
```

Requires .NET 8 / 9 / 10. HTTP is the default: `MapBuildingBlocksMcp()` → `/mcp`. Stdio (`UseStdioTransport()`) is console-only — logs on **stderr**. Do not enable stdio on a web API.

Cursor talks to a **running** API. After you add or rename tools, restart the host **and** reload the MCP server in Cursor. Aspire restart does not refresh Cursor’s cached `tools/list`.

Host OpenTelemetry: `IntegrateMcp = true` on `AddServiceDefaults` / `AddTelemetry`, plus `o.UseTelemetry()` on the MCP builder. `UseTelemetry(t => t.IncludeExceptionDetails = true)` is an explicit Development-only opt-in for exception text on `McpErrorCode.Internal`; it is not enabled automatically from the environment. Without `IntegrateMcp`, `mcp.tool` spans stay in-process.

## What this package is

Deny-by-default: only `[McpTool]` types/methods, `WithMcp`, and `MapTool` appear in `tools/list`. `inputSchema` comes from CLR (defaults/nullable = optional, enum members, `[Description]` / Swagger text when present). Successful calls return JSON text and `structuredContent`.

**MVC controllers are unsupported for now.** Do not put `[McpTool]` on a controller class or action.

This is **not** OpenAPI → MCP and **not** a SOLID linter. `[FromHeader]` DTOs stay HTTP-only.

## Host styles

| Style | When |
|--------|------|
| Scan + `UseDispatcher` | Mediator `ICommand` / `IQuery` (or any type you dispatch) |
| `[McpTool]` on a public static Minimal API method | Same method as `MapGet` / `MapPost` |
| `.WithMcp(app)` / `.WithMcp(app, name, description)` | Endpoint convention; optional if you already scan the attribute |
| `MapTool` | HTTP signature cannot be the MCP JSON body |

You can combine scan, `WithMcp`, and `MapTool` in one `AddBuildingBlocksMcp` call. Duplicate **names** are merged (first wins). Empty catalog throws `McpCatalogException` at first list/call.

---

## 1. Mediator / `ISender`

```csharp
[McpTool("orders.create", Description = "Create an order", Kind = McpToolKind.Command, Idempotent = true)]
public sealed record CreateOrder(int ProductId, int Quantity);

builder.Services.AddBuildingBlocksMcp(o =>
{
    o.ScanAssemblyContaining<CreateOrder>();
    o.UseTelemetry();
    o.UseMemoryIdempotency(TimeSpan.FromHours(1));
}).UseDispatcher(async (sp, msg, ct) =>
{
    await using var scope = sp.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<ISender>().Send(msg, ct);
});

app.MapBuildingBlocksMcp();
```

`UseDispatcher` is a **singleton**. Create a scope per invoke — `ISender` is scoped.

`Kind` may be omitted when the type implements `ICommand` / `IQuery`. Tool-level `Description` is required. Property `[Description]` is optional.

Lab: `orders.create` also sets `RequireConfirmation = true`. `demo.echo` sets `Idempotent = false` so smoke calls need no key.

---

## 2. Minimal API — same method as HTTP

JSON binds to **one** request parameter. Other parameters resolve from DI:

- `CancellationToken`
- `McpInvokeContext`
- interfaces / concrete services registered in DI
- `ILogger<T>`

`HttpContext` is **not** the MCP body. Outside HTTP it is null. Do not use types that only bind from headers as the MCP input.

GET query strings: use `[AsParameters]` on a small request type (lab `LabPingRequest`).

The handler must be a **public static** method-group. Lambdas and instance methods are not catalog tools (analyzer BBMCP005 for instance methods).

Pass the same `IEndpointRouteBuilder` you used to map the route into `WithMcp` (typically `app`, not a nested group that never becomes the MCP route source). `MapBuildingBlocksMcp` records that builder so the catalog can force endpoint construction; `WithMcp` conventions run lazily. Failures on unrelated routes (RequestDelegateFactory rejecting HTTP-only signatures) do not hide Scan/`MapTool` tools.

### A. `[McpTool]` + `ScanAssembly`

Scan is enough. You do not have to call `WithMcp`.

```csharp
[McpTool("lab.ping", Description = "Minimal API ping", Kind = McpToolKind.Query)]
public static string LabPing([AsParameters] LabPingRequest request)
    => string.IsNullOrWhiteSpace(request.Name) ? "pong" : $"pong:{request.Name}";

api.MapGet("/lab-ping", LabPing);

builder.Services.AddBuildingBlocksMcp(o =>
    o.ScanAssembly(Assembly.GetExecutingAssembly()));
```

### B. `[McpTool]` + `.WithMcp(app)`

Same tool. Scan and `WithMcp` **dedupe by name**. FeatureFusion uses this for `lab.ping`.

```csharp
api.MapGet("/lab-ping", LabPing).WithMcp(app);
```

`WithMcp()` without name/description **requires** `[McpTool]` on the method.

### C. `.WithMcp(app, name, description)` — no attribute

Kind is inferred from HTTP metadata:

| HTTP | Kind | Idempotency |
|------|------|-------------|
| GET | Query | never |
| POST or PUT | Command | store + `idempotencyKey` when registered |
| Other / mixed | Unspecified unless you set `Kind` in `configure` |

```csharp
api.MapPost("/items", CreateItem)
    .WithMcp(app, "items.create", "Create an item");

api.MapGet("/search", SearchItems)
    .WithMcp(app, "items.search", "Search items");

api.MapDelete("/items/{id}", DeleteItem)
    .WithMcp(app, "items.delete", "Delete an item", a => a.Kind = McpToolKind.Command);
```

### D. `MapTool` when HTTP cannot be the MCP input

Keep `GetCustomGreeting` HTTP-only if it uses `[FromHeader]` or several body shapes. Register a dedicated DTO:

```csharp
builder.Services.AddBuildingBlocksMcp(o =>
{
    o.ScanAssembly(Assembly.GetExecutingAssembly());
    o.MapTool<GreetingMcpRequest, string>(
        "greetings.custom",
        "Dedicated MCP DTO — not the HTTP FromHeader model",
        async (sp, msg, ctx, ct) =>
        {
            // scoped: validators, IFeatureManager, etc.
            return McpResult.Ok($"Hello {msg.Name}");
        },
        a => a.Kind = McpToolKind.Query);
});
```

There is also an overload without `IServiceProvider` when you do not need a scope.

---

## Idempotency

MCP has no HTTP method on a Mediator message. This package maps:

- **Command** ≈ POST/PUT (write)
- **Query** ≈ GET (read)

The invoker **never retries** a write. Idempotency is “same key → same stored success,” not a retry loop.

### When a key is required

| | Command | Query |
|--|---------|--------|
| Default `Idempotent` | `true` | ignored — store is never used |
| Store registered (`UseMemoryIdempotency`, `UseDistributedIdempotency`, or `IMcpIdempotencyStore`) | client **must** send `idempotencyKey` | no key in schema |
| `Idempotent = false` | no key (lab `demo.echo`) | — |

Without a store, command tools do not require a key (nothing to replay against). Register a store in any host that exposes write tools to agents.

### Schema vs runtime

The input schema advertises `idempotencyKey` as `string` with **`format: uuid`**. That is a hint for Cursor/Claude: generate a UUID. The host still accepts **any non-empty string** (ULID, opaque tokens).

Agents do not magically inject keys. If the field is required in `inputSchema`, the model fills it. If you opt out, do not ask the model for a key.

**New write:** new UUID. **Retry of the same write** (client timeout, disconnect): **reuse** the same key so the store returns the first success instead of creating a second order.

### Store behavior

`o.UseMemoryIdempotency(TimeSpan.FromHours(1))` registers a single in-process `MemoryIdempotencyStore` (optional TTL, process `SemaphoreSlim` wait-and-replay). Do not also `AddSingleton<IMcpIdempotencyStore>` unless you replace it.

Keys are namespaced as `toolName` + separator + client key so `orders.create` and another command cannot collide. Concurrent invokes with the same namespaced key share a `SemaphoreSlim`. Success is serialized to JSON and replayed as `JsonElement` (not `Deserialize<object>`). Queries never use the store or lock.

### Distributed (multi-instance)

`UseMemoryIdempotency` is process-local. Farms call `o.UseDistributedIdempotency(configure).UseRedisLock()`. Resolving `IMcpInvoker` throws if the lock is missing — unlocked distributed execution is not used. `UseRedisLock` registers MCP `RedisMcpIdempotencyLock` on the host `IConnectionMultiplexer` (does not register Redis). Custom `IMcpIdempotencyLock` remains supported instead of `UseRedisLock`.

Wait-and-replay (not HTTP Processing/409): Get completed payload → TryAcquire(owner token, lease) → if not acquired, poll Get / retry acquire until `AcquireWaitBudget` → Get again → InvokeCore if still missing → Set on success → Release in `finally`.

| Fact | Meaning |
|------|---------|
| Lock is required | `IDistributedCache` Get/Set alone does not serialize in-flight work. Resolving `IMcpInvoker` fails without a lock. |
| Lease (default 2 minutes) | In-flight safety window, **not** exactly-once. **No renewal in 1.1.0.** Keep it longer than the worst-case successful handler. |
| `AcquireWaitBudget` (default 30 seconds) | Waiters poll Get / retry acquire. Exhaustion returns MCP `Conflict`; the client should retry (replay if Set completed). This is **not** HTTP 409 Processing. |
| `PollDelay` (default 20 ms) | Delay between waiter poll attempts. |
| Lease expiry | Another instance may acquire and execute (overlap is allowed and characterized, not exactly-once). |
| Crash before Set | A later caller may execute again (at-least-once). |
| Set after successful InvokeCore fails | The computed success is still returned; persistence failure is logged; a later caller may execute again. |
| MCP vs HTTP keyspace | Payloads `mcp:idemp:{tool}\u001f{clientKey}`; lock `{payloadKey}:lock`. Not HTTP `Idempotency_*`. |
| Package boundary | BuildingBlocks.Mcp does not reference BuildingBlocks.Idempotency. Redis: `UseRedisLock` / `RedisMcpIdempotencyLock`. Custom SET NX: register `IMcpIdempotencyLock`. |

```csharp
o.UseDistributedIdempotency(opts =>
{
    opts.Lease = TimeSpan.FromMinutes(2);
    opts.PayloadTtl = TimeSpan.FromHours(1);
    opts.AcquireWaitBudget = TimeSpan.FromSeconds(30);
    opts.PollDelay = TimeSpan.FromMilliseconds(20);
})
.UseRedisLock();
```

### Confirmation (2026 MRTR)

`RequireConfirmation = true` (lab `orders.create`) adds required `confirmed: true` in the schema. The invoker still rejects missing confirmation with `McpErrorCode.ConfirmationRequired`.

On MCP **2026-07-28** (`IsMrtrSupported`), that error is translated by the protocol adapter to SDK `InputRequiredException` / `resultType: input_required`: elicitation for `confirmed`, `requestState` `awaiting-confirmation` (opaque echo, not a server session). Accept sets `McpInvokeContext.Confirmed` and invokes once. Decline returns `ConfirmationRequired` JSON without invoking. MCP **2025-11-25** clients still receive `ConfirmationRequired` JSON. Sending `confirmed: true` skips elicitation on both revisions. Official C# clients auto-retry elicitation when `ElicitationHandler` is set. This is the `confirmed` write gate only — not a general elicitation or OAuth framework.

### Filters and limits

Optional `IMcpToolFilter` and `IMcpRateLimiter`. The default limiter allows all; register a real one to reject storms (`McpErrorCode.RateLimited`).

---

## Lab (FeatureFusion)

Development only (`http://localhost:5141/mcp`):

| Tool | Style | HTTP analogue |
|------|--------|----------------|
| `demo.echo` | Scan + `ISender`, `Idempotent = false` | `POST /api/v1/mediator-demo/echo` |
| `orders.create` | Scan + `ISender`, idempotent + confirmation | `POST /api/v1/Order/order` |
| `products.list` | Scan + `ISender` (query) | products query |
| `lab.ping` | `[McpTool]` + `.WithMcp(app)` on `LabPing` | `GET /api/v1/lab-ping` |

Lab Compose and AppHost set `ASPNETCORE_ENVIRONMENT=Development` so HTTP MCP is mapped at `/mcp`. A Production-environment host still registers MCP services (so `.WithMcp` does not crash startup) but does not map `/mcp`.

Workspace root for this Cursor window is `src/`. Put this in `src/.cursor/mcp.json` (gitignored; copy locally):

```json
{
  "mcpServers": {
    "featurefusion": {
      "url": "http://localhost:5141/mcp"
    }
  }
}
```

If Cursor still lists three tools after you added `lab.ping`, reload the MCP server — the session is stale.

## Analyzers

Packed in the NuGet: BBMCP001–005 (missing description, instance methods, etc.). BBMCP002 (idempotent commands) remains in `SupportedDiagnostics`; commands default to idempotent so it is not a typical day-to-day warning.

## Docs

- [Test matrix](MCP_TEST_MATRIX.md)
- [Cookbook](cookbook.md) (short recipes)
- [CHANGELOG](../../CHANGELOG.md)
