# web/

**Next.js frontend showcase** for the FeatureFusion lab. This directory is **not** in `FeatureFusion.sln` — the solution stays .NET-only.

The lab API, Aspire AppHost, and BuildingBlocks packages live under `src/`. This directory is the TypeScript UI that calls the Demo Commerce HTTP surfaces and the pagination keyset route. It is the **evidence made visible** — every feature map back to an experiment or package the lab proved.

## Tech stack

| Layer | Choice |
|-------|--------|
| Framework | Next.js (App Router) |
| Styling | Tailwind CSS + shadcn/ui |
| Data | `fetch` via `lib/fetcher.ts` |
| State | React hooks + context (sidebar provider) |
| Package manager | Yarn |

## What this showcases

Five features, ordered by lab value. Each one maps directly to a BuildingBlock or experiment the lab proved on real infrastructure.

### 1. Cursor pagination storefront — the demo

The flagship. An infinite-scroll product catalog driven by opaque keyset cursors.

- **Endpoint:** `GET /api/v1/products-page?limit=20&sortBy=Price&sortDirection=Ascending`
- **Sort switcher:** Id · Name · Price · CreatedAt, Ascending / Descending
- **Bidirectional navigation:** forward via `NextCursor`, backward via `PreviousCursor`
- **Cursors are opaque** — pass them back unchanged; do not construct them

This is exactly what **Experiments 1–2** stress-tested (careless cursor clients). A UI that passes cursors back unchanged is the evidence made visible.

**Mapped to:** [BuildingBlocks.Pagination.EntityFrameworkCore](https://www.nuget.org/packages/BuildingBlocks.Pagination.EntityFrameworkCore) 1.1.0 — typed `SortKey`, composite keyset order, Npgsql row comparison, `NULLS FIRST/LAST`.

### 2. Idempotent checkout flow

A retry-safe order creation flow that demonstrates HTTP idempotency as a visible UX state machine.

- **Endpoint:** `POST /api/v1/Order/order` with a client-generated `Idempotency-Key` (UUID)
- **Retry on network failure:** the UI resends the same key and shows the 2xx envelope replay — no double order
- **Visible statuses:**
  - `200` — created
  - `202` — AdmissionPending (polling state; the lab's Allow/Defer/Deny admission gate becomes a visible state)
  - `409` — conflict

**Mapped to:** [BuildingBlocks.Idempotency](https://www.nuget.org/packages/BuildingBlocks.Idempotency) 1.0.1 — 2xx envelope replay, ProblemDetails, optional Redis lock. Proven by **Experiments 3, 4, 12**.

### 3. MCP "agent console"

A chat-like panel where a user "asks" the app and the UI renders MCP tool calls + results. The most distinctive idea — it turns the lab's MCP research into a live demo rather than a static screenshot.

- **Endpoint:** `/mcp` with tools `products.list`, `orders.create`, `catalog.*`, `customers.*`
- **UX:** user types a request → UI calls the MCP server → renders tool name, parameters, and result inline
- **Fits the repo's MCP/MAF research thread:** the Cursor screenshot in the root README is a static proof; this is a live one

**Mapped to:** [BuildingBlocks.Mcp](https://www.nuget.org/packages/BuildingBlocks.Mcp) 1.1.0 — `[McpTool]` / `MapTool` → MCP tools at `/mcp`.

### 4. Feature-flag–gated UI

Login with VIP credentials to see conditional components render based on claim filters. The VIP greeting demo becomes end-user-visible.

- **Login:** `POST /api/v1/Auth/login` with `vipuser` / `vippassword`
- **Gated component:** VIP-only greeting rendered when the JWT contains the right claim
- **Feature filter preview:** `GET /api/v1/lab/feature-filter-preview`

**Mapped to:** Lab Feature Management + custom claims filters. Not a BuildingBlock — this is lab-only evidence.

### 5. Trace correlation story

Propagate W3C `traceparent` from the browser `fetch` → API → SigNoz, then show "view this request in SigNoz" links. **Experiment 18** proved traces don't cross RabbitMQ — a UI that shows the HTTP-leg correlation fills a real documentation gap.

- **Propagation:** `fetch` includes `traceparent` header → API picks it up → SigNoz link in the UI
- **SigNoz UI:** `http://localhost:8080` (Aspire-provisioned)

**Mapped to:** [BuildingBlocks.Telemetry](https://www.nuget.org/packages/BuildingBlocks.Telemetry) 1.0.2 + [BuildingBlocks.Aspire.Hosting.SigNoz](https://www.nuget.org/packages/BuildingBlocks.Aspire.Hosting.SigNoz) 1.0.0.

## Project structure

```text
web/
├── app/
│   ├── layout.tsx              # Root layout (sidebar + providers)
│   ├── page.tsx                # Home page
│   ├── catalog/
│   │   ├── page.tsx            # Catalog page (server component)
│   │   └── _components/
│   │       └── catalog.tsx     # Catalog client component
│   └── globals.css
├── components/
│   ├── sidebar/                # App sidebar navigation
│   └── ui/                     # shadcn/ui primitives
├── hooks/                      # Custom React hooks
├── lib/
│   ├── fetcher.ts              # Typed fetch wrapper
│   ├── constants/              # Route constants
│   └── utils.ts                # cn() and helpers
├── models/                     # TypeScript type definitions
├── providers/                  # React context providers
├── public/                     # Static assets
├── package.json
├── tsconfig.json
├── next.config.ts
└── postcss.config.mjs
```

## Getting started

```bash
cd web
yarn install
yarn dev
```

The UI expects the FeatureFusion API to be running (via Aspire AppHost or Docker Compose). Point it at the API's HTTP endpoint — the Aspire dashboard shows the dynamic port.

## Design principles

1. **Evidence-driven:** every feature maps to a proven experiment or package. No cargo-cult UI.
2. **Consume, don't duplicate:** the UI calls the public HTTP/MCP APIs. It does not re-implement BuildingBlocks contracts in the client.
3. **Out of the .sln:** the .NET solution stays .NET-only unless there is a deliberate reason to bind them.
4. **Opaque cursors:** pagination cursors are passed back unchanged. The UI never decodes or constructs them.
