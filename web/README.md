# web/

Reserved **Next.js** project root for a FeatureFusion frontend showcase. This folder is **not** in `FeatureFusion.sln` (the solution stays .NET-only).

The lab API, Aspire AppHost, and BuildingBlocks packages live under `src/`. This directory is the place to add a TypeScript UI later that calls the Demo Commerce HTTP surfaces (`GET /api/v1/catalog/*`, `/api/v1/customers/{id}/cart`, checkout, `/api/v1/orders`) and the pagination-lab keyset route (`GET /api/v1/products-page`).

## Status

Not scaffolded yet. Do not assume `package.json` or `npm` scripts exist.

When an app is added here:

1. Keep it out of the .NET solution unless there is a deliberate reason to bind them.
2. Point the UI at the Aspire-assigned FeatureFusion HTTP endpoint (dynamic ports locally).
3. Do not duplicate BuildingBlocks contracts in the client — consume the public HTTP/MCP APIs.
