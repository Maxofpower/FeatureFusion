# BuildingBlocks.Mcp

Map **application message types** (commands, queries, DTOs) and **public static Minimal API methods** to MCP tools on the official C# SDK. Deny-by-default catalog, typed `McpResult`, HTTP by default (stdio opt-in). Does not require BuildingBlocks.Mediator.

[![NuGet](https://img.shields.io/nuget/v/BuildingBlocks.Mcp.svg)](https://www.nuget.org/packages/BuildingBlocks.Mcp)
[![GitHub Release](https://img.shields.io/github/v/release/Maxofpower/FeatureFusion?filter=mcp-v*&logo=github&label=GitHub%20Release)](https://github.com/Maxofpower/FeatureFusion/releases?q=mcp-v)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**When to use:** Cursor or Claude should call the **same logic** as HTTP. Opt-in with `[McpTool]` on a message type **or** a public static Minimal API method (`WithMcp` optional), or use `MapTool`. Unmarked types/methods are never tools. **MVC controllers are unsupported for now.** HTTP-only inputs (`FromHeader`) cannot be the MCP body.

## What's new in 1.1.0

- **Distributed write idempotency:** `UseDistributedIdempotency` + `UseRedisLock` (wait-and-replay across instances; not HTTP Processing/409). Completed payloads on host `IDistributedCache`; in-flight SET NX PX lease via MCP `RedisMcpIdempotencyLock` on the host `IConnectionMultiplexer` (default 2 minutes, **no renewal** — not exactly-once). Fail-closed if the lock is missing. Custom `IMcpIdempotencyLock` still works. Wait-budget exhaustion (`AcquireWaitBudget`, default 30 seconds) is MCP `Conflict` (client retries). MCP keys `mcp:idemp:…` are distinct from HTTP `Idempotency_*`. This package does not reference `BuildingBlocks.Idempotency`.
- **2026 MRTR confirmation:** unconfirmed `RequireConfirmation` writes elicit `confirmed` via SDK `InputRequiredException` when the client is MCP `2026-07-28` (`requestState` `awaiting-confirmation`, opaque echo). Accept invokes; decline returns `ConfirmationRequired` without invoking. `2025-11-25` still returns `ConfirmationRequired` JSON. `confirmed: true` skips elicitation.

## What's in 1.0.0

- Message types or public static endpoint methods as MCP tools (`[McpTool]`, deny-by-default scanner), **or** `MapTool` handlers
- Scoped `MapTool` overload: handler receives `IServiceProvider` from a new DI scope
- Idempotency: `UseMemoryIdempotency(ttl)` on the builder; commands (POST/PUT) require `idempotencyKey` (`format: uuid` in the schema; any non-empty string accepted); queries never use the store. Namespaced keys, in-process wait-and-replay, `JsonElement` replay.
- `inputSchema` from CLR: defaults/nullable = optional, enum members, `[Description]` / Swagger parameter text
- Successful calls return JSON text **and** `structuredContent`
- Safe writes: `idempotencyKey`, confirmation, timeout, `IMcpToolFilter`, `IMcpRateLimiter`, `catalog://tools`
- Analyzers BBMCP001–005 packed in the NuGet
- Opt-in ActivitySource `BuildingBlocks.Mcp` (`UseTelemetry()`). `IncludeExceptionDetails` is explicit opt-in (Development only recommended); it is not auto-enabled by environment.

## Install

```bash
dotnet add package BuildingBlocks.Mcp
```

Requires **.NET 8**, **.NET 9**, or **.NET 10**. Host OpenTelemetry: `BuildingBlocks.Telemetry` with `IntegrateMcp = true`.

After you add or rename tools, **restart the API and reload the MCP server in Cursor**. Aspire restart alone does not refresh Cursor’s cached `tools/list`.

## Quick start

```csharp
[McpTool("orders.create", Description = "Create an order")]
public sealed record CreateOrder(int ProductId, int Quantity);

builder.Services.AddBuildingBlocksMcp(o =>
{
    o.ScanAssemblyContaining<CreateOrder>();
    o.UseMemoryIdempotency(TimeSpan.FromHours(1));
}).UseDispatcher(async (sp, msg, ct) =>
{
    await using var scope = sp.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<ISender>().Send(msg, ct);
});

app.MapBuildingBlocksMcp(); // Cursor: { "url": "http://localhost:5141/mcp" }
```

API must be running. Reload the MCP server in Cursor after tool changes. Minimal API / `MapTool` / stdio: sections below.

## Quick start — Mediator / `ISender`

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

app.MapBuildingBlocksMcp(); // HTTP /mcp — Cursor url (API must already be running)
```

`UseDispatcher` is a singleton; create a **scope** before resolving scoped `ISender`. `Kind` can be omitted when the type implements Mediator `ICommand` / `IQuery`. Tool-level `Description` is required.

You can scan **and** `MapTool` in the same `AddBuildingBlocksMcp` call. Scanned types use `UseDispatcher`; scanned methods invoke via DI; `MapTool` uses its handler.

## Minimal API — registration approaches

Use a **public static** method-group (the same method you pass to `MapGet` / `MapPost`). JSON binds to **one** request parameter. `CancellationToken`, `McpInvokeContext`, interfaces, and `ILogger<T>` come from DI. `HttpContext` is not the MCP body (null outside HTTP). GET query strings: bind with `[AsParameters]` on a request type. Property `[Description]` is optional (schema text only).

**MVC controllers are unsupported for now** — do not put `[McpTool]` on a controller class or action.

### A. `[McpTool]` + assembly scan

The scanner finds public static methods with `[McpTool]`. You do not need `WithMcp` for the catalog.

```csharp
[McpTool("lab.ping", Description = "Minimal API ping", Kind = McpToolKind.Query)]
public static string LabPing([AsParameters] LabPingRequest request)
    => string.IsNullOrWhiteSpace(request.Name) ? "pong" : $"pong:{request.Name}";

api.MapGet("/lab-ping", LabPing);

builder.Services.AddBuildingBlocksMcp(o => o.ScanAssembly(Assembly.GetExecutingAssembly()));
app.MapBuildingBlocksMcp();
```

### B. `[McpTool]` + `.WithMcp(app)`

Same tool as A. `WithMcp` registers from the endpoint convention. If you also scan, the catalog **dedupes by tool name**. Pass the `IEndpointRouteBuilder` used for `MapGet` (the app or group root, not a nested group that is not the MCP host).

```csharp
api.MapGet("/lab-ping", LabPing).WithMcp(app);
```

### C. `.WithMcp(app, name, description)` — no attribute

Kind is inferred from the HTTP verb: **GET → Query** (no idempotency key), **POST/PUT → Command** (idempotent write when a store is registered). Other verbs: set `Kind` in `configure`.

```csharp
api.MapPost("/items", CreateItem)
    .WithMcp(app, "items.create", "Create an item");

api.MapGet("/search", SearchItems)
    .WithMcp(app, "items.search", "Search items");
```

### D. `MapTool` — HTTP DTO cannot be the MCP input

Use when the HTTP signature uses `[FromHeader]`, multiple body shapes, or other bindings that are not a single JSON object. Keep the HTTP method HTTP-only; map a dedicated DTO.

```csharp
o.MapTool<GreetingMcpRequest, string>(
    "greetings.custom",
    "Dedicated MCP DTO — not the HTTP FromHeader model",
    async (sp, msg, ctx, ct) => McpResult.Ok("…"),
    a => a.Kind = McpToolKind.Query);
```

The `IServiceProvider` overload creates a **scope** per call (validators, feature flags, scoped services).

## Idempotency (writes only)

MCP has no HTTP verb on Mediator messages. Treat **Command ≈ POST/PUT** and **Query ≈ GET**. The library **never retries** writes.

| | Command | Query |
|--|---------|--------|
| Default | `Idempotent = true` | never uses the store |
| Client | must send `idempotencyKey` when a store is registered | no key |
| Schema | `string` + `format: uuid` (hint) | no key property |
| Runtime | any non-empty string (UUID preferred; ULID is fine) | — |
| Opt out | `Idempotent = false` | — |

Register the in-memory store (single process) on the builder — do not add `IMcpIdempotencyStore` by hand unless you have a custom implementation:

```csharp
o.UseMemoryIdempotency(TimeSpan.FromHours(1));
```

Multi-instance hosts call `UseDistributedIdempotency` **and** `UseRedisLock` (host already has `IDistributedCache` + `IConnectionMultiplexer`). A shared Get/Set cache without a lock does not serialize in-flight calls. Waiters poll and replay; they do not get HTTP Processing/409 while the lease is valid. Custom locks: register `IMcpIdempotencyLock` instead of `UseRedisLock`.

```csharp
o.UseDistributedIdempotency(opts =>
{
    opts.Lease = TimeSpan.FromMinutes(2);                 // in-flight window; no renewal
    opts.PayloadTtl = TimeSpan.FromHours(1);
    opts.AcquireWaitBudget = TimeSpan.FromSeconds(30);  // then MCP Conflict — client retries
    opts.PollDelay = TimeSpan.FromMilliseconds(20);
})
.UseRedisLock(); // RedisMcpIdempotencyLock over host IConnectionMultiplexer
```

The lease (default 2 minutes) is the in-flight safety window, **not** exactly-once execution — there is no lease renewal in 1.1.0. Lease expiry, a crash before Set, or a Set failure after a successful invoke can cause a second execution (at-least-once). If waiters exhaust `AcquireWaitBudget` (default 30 seconds) they receive MCP `Conflict` and should retry (replay if Set completed). Payload keys use `mcp:idemp:{tool}\u001f{clientKey}` (lock key `{payloadKey}:lock`), distinct from HTTP `Idempotency_*`. This package does not reference `BuildingBlocks.Idempotency`. Queries never use the store or lock. Successful results are stored as JSON and replayed as `JsonElement`.

## Confirmation (MRTR)

`RequireConfirmation = true` adds required `confirmed: true` in the schema. The invoker still rejects missing confirmation with `McpErrorCode.ConfirmationRequired`.

On MCP **2026-07-28** (`IsMrtrSupported`), that error is translated to SDK `InputRequiredException`: elicitation for `confirmed`, `requestState` `awaiting-confirmation` (opaque echo, not a session). Accept sets `Confirmed` and invokes once; decline returns `ConfirmationRequired` without invoking. On MCP **2025-11-25**, the tool error stays JSON. Sending `confirmed: true` skips elicitation on both revisions. Official C# clients auto-retry elicitation when `ElicitationHandler` is set.

Cursor/Claude fill `idempotencyKey` because the schema marks it **required**. They do not invent a key unless the field exists. Generate a **new UUID** for a new write; **reuse** the same key only when retrying that same write (timeouts, disconnects). Lab `orders.create` uses confirmation + a key. Lab `demo.echo` uses `Idempotent = false` so smoke calls need no key.

## Transport and Cursor

HTTP is the default. Stdio is opt-in for **console** hosts only: `o.UseStdioTransport()`. Log to **stderr**. Do not enable stdio on a web API.

Cursor HTTP (API must already be running):

```json
{
  "mcpServers": {
    "myapi": {
      "url": "http://localhost:5141/mcp"
    }
  }
}
```

## Features

- **Deny-by-default:** only `[McpTool]` types/methods and `MapTool` / `WithMcp` registrations
- **Host styles:** `Scan` + `UseDispatcher`, `[McpTool]` on static endpoint methods, `WithMcp`, or `MapTool`
- **Typed `McpResult` / `McpError`**
- **HTTP default:** `MapBuildingBlocksMcp()` → `/mcp`
- **Schema agents can use:** optional vs required, JSON Schema `enum`, property descriptions
- **`structuredContent`** on success
- **Catalog resource:** `catalog://tools`
- **Roslyn analyzers** BBMCP001–005
- **`UseTelemetry()`:** ActivitySource `BuildingBlocks.Mcp`. Optional `t => t.IncludeExceptionDetails = true` includes exception messages on `McpErrorCode.Internal` (keep off in production). Host export still requires `IntegrateMcp = true` on `AddTelemetry` / `AddServiceDefaults`.

## What it is not (v1)

- **MVC controllers are unsupported for now** (actions, `[FromHeader]`, `ActionResult`). Use public static Minimal API methods, message types, or `MapTool`.
- Not OpenAPI → MCP, not a SOLID linter. `[FromHeader]` DTOs stay HTTP-only.
- No prompts, OAuth, or MCP Apps. Confirmation elicitation is only the 2026 MRTR `confirmed` flow above — not a general elicitation framework.
- Do not call `UseStdioTransport()` on a web API
- Production hosts should leave MCP unmapped (FeatureFusion registers it only in Development)

## Docs

- [MCP guide](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/mcp.md)
- [ADR 0002](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/adr/0002-mcp-message-tools.md)
- [Test matrix](https://github.com/Maxofpower/FeatureFusion/blob/main/docs/building-blocks/MCP_TEST_MATRIX.md)
- [CHANGELOG](https://github.com/Maxofpower/FeatureFusion/blob/main/CHANGELOG.md)

Demo host: **FeatureFusion** (`orders.create`, `products.list`, `demo.echo`, `lab.ping` at `http://localhost:5141/mcp` in Development).

## License

MIT — Copyright (c) 2026 Mohammad Hasan Hosseini
