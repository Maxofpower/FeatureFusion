# MCP test matrix

xUnit on **net8.0 / net9.0 / net10.0**. No coverlet gate. CI: `.github/workflows/mcp.yml`. Release pack: `.github/workflows/mcp-release.yml` (tag `mcp-v*`).

| Area | Tests |
|------|--------|
| Scanner | Only `[McpTool]`; duplicate names throw |
| Schema | Optional vs required (defaults / nullable / `[Required]`); enum names vs integer values; `[Description]` |
| Protocol schema | `ToTool` JSON: `enum`, optional not in `required`; idempotent commands advertise `idempotencyKey` with `format: uuid` |
| Kind | Query vs Command |
| Invoke | JSON → DTO → result |
| Deny-by-default | Unmarked types not scanned |
| Delegate | `UseDispatcher` (host creates DI scope for `ISender`) |
| MapTool | Handler without Mediator; scoped `IServiceProvider` overload (`ValidateScopes`) |
| Endpoint method | `[McpTool]` and/or `WithMcp` on public static method; JSON → one request param. MVC controllers unsupported. |
| Catalog URI | `catalog://tools` and `catalog://tools/` |
| Aspire live HTTP | `FeatureFusionMcpTests` — tools/list (`demo.echo`, `products.list`, `orders.create`, `lab.ping`), echo, orders.create, products.list schema, `structuredContent`, catalog://tools, `lab.ping` |
| Cursor HTTP | `src/.cursor/mcp.json` → `http://localhost:5141/mcp`; API must be running (see [`mcp.md`](mcp.md)) |
| Idempotency | Commands only; `UseMemoryIdempotency`; missing key; store prevents double dispatch; namespaced keys; TTL; in-flight lock; `JsonElement` replay; queries ignore store |
| Rate limit | Deny → `RateLimited` |
| Confirm / timeout | ConfirmationRequired; Timeout |
| Filter | Hidden from list and invoke |
| Throw | Internal without stack |
| No write retry | Single invoke on throw |
| McpPage | `items` / `nextCursor` |
| Duck Result | Failure maps to Validation |
| Analyzers | BBMCP001–005 |
