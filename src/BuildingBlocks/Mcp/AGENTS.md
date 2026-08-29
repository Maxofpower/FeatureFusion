# BuildingBlocks.Mcp — agent notes

Message types, public static endpoint methods, or `MapTool` handlers → MCP tools. Install: `dotnet add package BuildingBlocks.Mcp`.

## When to choose this

Cursor/Claude should call the **same logic** as HTTP. Opt-in with `[McpTool]` on a command/query **or** on a public static Minimal API method (`MapGet`/`MapPost` + optional `.WithMcp()`). Use `MapTool` when the HTTP DTO cannot be the MCP input (`FromHeader`, etc.). **MVC controllers are unsupported for now.** Do not use for architecture/SOLID analysis.

HTTP is the default (`MapBuildingBlocksMcp` → `/mcp`). Stdio is opt-in via `UseStdioTransport()` on a **console** host — log to **stderr** only. Do not enable stdio on a web API.

After tool add/rename: restart the API **and** reload this MCP server. Cursor caches `tools/list`.

## Mediator host

```csharp
builder.Services.AddBuildingBlocksMcp(o =>
{
    o.ScanAssemblyContaining<CreateOrder>();
    o.UseMemoryIdempotency(TimeSpan.FromHours(1));
})
    .UseDispatcher(async (sp, msg, ct) =>
    {
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(msg, ct);
    });
app.MapBuildingBlocksMcp();
```

Create a DI **scope** per call (`ISender` is scoped; the dispatcher is singleton). `Kind` is optional when the type is `ICommand` / `IQuery`. Tool `Description` is required.

## Minimal API (no Mediator)

Public static method-group only. JSON → **one** request parameter. DI: `CancellationToken`, `McpInvokeContext`, interfaces, `ILogger<T>`. `HttpContext` is skipped (null outside HTTP). Do not use `[FromHeader]` types as the MCP body. GET: `[AsParameters]` on a request type.

Four registration styles (same catalog; scan + `WithMcp` **dedupe by name**):

1. `[McpTool]` on the method + `ScanAssembly` — enough; `WithMcp` optional.
2. `[McpTool]` + `.WithMcp(app)` — pass the `IEndpointRouteBuilder` used for `MapGet`.
3. `.WithMcp(app, "name", "description")` — no attribute. GET → Query; POST/PUT → Command.
4. `MapTool<TMessage, TResult>` — dedicated MCP DTO when HTTP cannot be the input. Scoped `IServiceProvider` overload for validators / feature flags.

**MVC controllers are unsupported for now.** Property `[Description]` is optional (JSON Schema text only).

```csharp
[McpTool("lab.ping", Description = "Ping", Kind = McpToolKind.Query)]
public static string LabPing([AsParameters] LabPingRequest request)
    => string.IsNullOrWhiteSpace(request.Name) ? "pong" : $"pong:{request.Name}";

api.MapGet("/lab-ping", LabPing).WithMcp(app);
```

```csharp
api.MapPost("/items", CreateItem).WithMcp(app, "items.create", "Create an item");
```

## Idempotency

Writes (Command / POST / PUT) are never retried. Commands default to requiring `idempotencyKey` when `UseMemoryIdempotency` (or another `IMcpIdempotencyStore`) is registered. Schema `format: uuid` is a hint; any non-empty string is accepted. Queries never use the store. `Idempotent = false` opts a command out (lab `demo.echo`). Keys are namespaced per tool; in-flight calls lock; success replays as `JsonElement`. Multi-instance: Redis via `IMcpIdempotencyStore`. `RequireConfirmation` adds required `confirmed: true`.

Agents: send a **new UUID** for a new write; **reuse** the key only on retry of that write. Do not invent keys for query tools.

Optional `IMcpToolFilter`, `IMcpRateLimiter`.
