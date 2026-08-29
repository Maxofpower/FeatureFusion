# ADR 0002 — BuildingBlocks.Mcp maps message types to MCP tools

- **Status:** Accepted
- **Date:** 2026-08-28
- **Deciders:** Mohammad Hasan Hosseini
- **Related:** [docs/building-blocks/mcp.md](../building-blocks/mcp.md)

## Decision

1. `BuildingBlocks.Mcp` is a standalone NuGet (net8/net9/net10). It does not reference Mediator, Telemetry, FeatureFusion, Polly, or Feature Management.
2. Tools are **opt-in** `[McpTool]` on message types **or** public static endpoint methods, plus optional `MapTool`. Not OpenAPI, not controller classes. Transport is the official `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` SDK (stdio + Streamable HTTP).
3. Public invoke surface is typed: `McpResult<T>`, `McpError`, `MapTool<TMessage,TResult>`. Business failures are results. Infrastructure faults map to `McpErrorCode.Internal` without stack traces by default.
4. Deny-by-default catalog, fail-fast at startup (`McpCatalogException`). Writes are never retried inside the library; idempotency is the write story.
5. Hosts wire `UseDispatcher` (scoped `ISender.Send(object)` for Mediator) and/or `[McpTool]` on static methods and/or `MapTool`. There is no Mediator adapter package.
6. FeatureFusion is a demo (`demo.echo`, `orders.create`, `products.list` via scan; `lab.ping` on a Minimal API method). Hosts map lab `Result<T>` via `IMcpResultMapper`.

## Consequences

Cursor/Claude can call the same handlers as HTTP without OpenAPI proxies. Official SDK owns protocol; this package owns catalog, typing, filters, and safe defaults.
