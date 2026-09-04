# Lab integration experiments

Behavioral experiments against **real** FeatureFusion workflows hosted by [`AspireFixture`](../Aspire/AspireFixture.cs). Each experiment is an integration test with a stated hypothesis, labeled invocations, captured evidence, a JSON artifact, and explicit invariants.

**Lab charter (what the laboratory is for, evidence families, when to extract a BuildingBlock):** [`docs/lab/README.md`](../../../../docs/lab/README.md).

This document describes what the Lab **does today**. It is not a framework specification and does not prescribe a Scenario/Actor engine.

## Shared observation helpers (Phase 2)

Lab-only, not NuGet. Used when ≥3 experiments share identical observation semantics:

| Helper | Location | Consumers (Exp #) |
|--------|----------|-------------------|
| `HttpOrderCreate` | [`Infrastructure/Orders/HttpOrderCreate.cs`](../Infrastructure/Orders/HttpOrderCreate.cs) | 3, 4, 5, 8, 12, 15, 18 |
| `OrderOutboxObserver` | [`Infrastructure/Orders/OrderOutboxObserver.cs`](../Infrastructure/Orders/OrderOutboxObserver.cs) | 8, 10, 13, 14, 15, 16, 18 |
| `McpToolResults` | [`Infrastructure/Mcp/McpToolResults.cs`](../Infrastructure/Mcp/McpToolResults.cs) | 2, 6, 10, 13, 14, 16 |

Also: prefer `LabTrace` / `LabRunInfo` over local copies of `IsMediator` / `IsNpgsql` / `ReadGitSha`.

## Evidence families (summary)

| Family | Experiments | Notes |
|--------|-------------|-------|
| Pagination abuse | 1–2 | HTTP + MCP keyset clients |
| HTTP Redis idempotency | 3, 4, 12, 15 | → BuildingBlocks.Idempotency provenance |
| MCP writes / agent clients | 6, 13, 14, 16 | Idempotency + rate-limit bound |
| Outbox happy path | 5, 8, 10 | HTTP + MCP parity |
| Consumer dedup / DLQ | 7, 9, 11, 17 | Inbox + `processed_messages` |
| Telemetry / correlation | TraceEvidence, 18 | W3C does not cross RabbitMQ today |
| MAF + Ollama | Prototype only | Not numbered |

## Purpose

- **Behavioral experiments**, not unit tests of libraries in isolation. The application under test is the in-process lab API (`WebApplicationFactory<Program>`) with Aspire containers for Postgres, Redis, RabbitMQ, and Memcached.
- **Production code unchanged.** Experiments observe production paths; only test infrastructure (fixture decoration, observation helpers) may differ.
- **Integration evidence over mocks.** Prefer HTTP/MCP responses, in-process `Activity` capture, and fixture observation hooks over synthetic stand-ins for collaborators.
- **One falsifiable hypothesis per experiment**, expressed in the test class XML documentation and asserted at the end of the `[Fact]`.

Related but separate: [`FeatureFusionTraceEvidenceTests`](../Api/FeatureFusionTraceEvidenceTests.cs) (Phase 1) proves the in-process trace evidence path before the numbered experiment series. It lives under `Api/`, not `Experiments/`.

Smoke and direct EventBus tests live under [`EventBus/`](../EventBus/) and [`Api/`](../Api/). They validate wiring; experiments validate **observable production behavior** under deliberate client/envelope conditions.

---

## Experiment catalog

| # | Artifact name | Folder / test class |
|---|---------------|---------------------|
| 1 | `pagination-cursor-abuse-v1` | [`PaginationAbuse/PaginationAbuseExperimentTests.cs`](PaginationAbuse/PaginationAbuseExperimentTests.cs) |
| 2 | `mcp-products-list-cursor-abuse-v1` | [`PaginationAbuse/McpPaginationAbuseExperimentTests.cs`](PaginationAbuse/McpPaginationAbuseExperimentTests.cs) |
| 3 | `order-create-redis-idempotency-v1` | [`CacheVsProduction/CacheVsProductionExperimentTests.cs`](CacheVsProduction/CacheVsProductionExperimentTests.cs) |
| 4 | `order-create-idempotency-concurrency-v1` | [`Concurrency/ConcurrencyRaceExperimentTests.cs`](Concurrency/ConcurrencyRaceExperimentTests.cs) |
| 5 | `http-order-outbox-delivery-v1` | [`OutboxDelivery/OutboxDeliveryExperimentTests.cs`](OutboxDelivery/OutboxDeliveryExperimentTests.cs) |
| 6 | `mcp-orders-create-idempotency-v1` | [`McpOrderIdempotency/McpOrderIdempotencyExperimentTests.cs`](McpOrderIdempotency/McpOrderIdempotencyExperimentTests.cs) |
| 7 | `duplicate-integration-event-delivery-v1` | [`DuplicateDelivery/DuplicateDeliveryExperimentTests.cs`](DuplicateDelivery/DuplicateDeliveryExperimentTests.cs) |
| 8 | `http-order-outbox-lifecycle-v1` | [`OutboxLifecycle/OutboxLifecycleExperimentTests.cs`](OutboxLifecycle/OutboxLifecycleExperimentTests.cs) |
| 9 | `event-bus-failure-outcome-v1` | [`EventBusFailure/EventBusFailureExperimentTests.cs`](EventBusFailure/EventBusFailureExperimentTests.cs) |
| 10 | `mcp-order-outbox-parity-v1` | [`McpOrderOutbox/McpOrderOutboxExperimentTests.cs`](McpOrderOutbox/McpOrderOutboxExperimentTests.cs) |
| 11 | `handler-throw-failure-outcome-v1` | [`HandlerThrowFailure/HandlerThrowFailureExperimentTests.cs`](HandlerThrowFailure/HandlerThrowFailureExperimentTests.cs) |
| 12 | `order-create-idempotency-fingerprint-v1` | [`IdempotencyFingerprint/IdempotencyFingerprintExperimentTests.cs`](IdempotencyFingerprint/IdempotencyFingerprintExperimentTests.cs) |
| 13 | `mcp-orders-create-agent-key-regeneration-v1` | [`McpAgentKeyRegeneration/McpAgentKeyRegenerationExperimentTests.cs`](McpAgentKeyRegeneration/McpAgentKeyRegenerationExperimentTests.cs) |
| 14 | `mcp-orders-create-concurrent-same-key-v1` | [`McpConcurrentSameKey/McpConcurrentSameKeyExperimentTests.cs`](McpConcurrentSameKey/McpConcurrentSameKeyExperimentTests.cs) |
| 15 | `order-create-idempotency-processing-lease-v1` | [`IdempotencyProcessingLease/IdempotencyProcessingLeaseExperimentTests.cs`](IdempotencyProcessingLease/IdempotencyProcessingLeaseExperimentTests.cs) |
| 16 | `mcp-orders-create-tool-storm-rate-limit-v1` | [`McpToolStormRateLimit/McpToolStormRateLimitExperimentTests.cs`](McpToolStormRateLimit/McpToolStormRateLimitExperimentTests.cs) |
| 17 | `event-bus-enable-deduplication-processed-messages-v1` | [`ProcessedMessageDeduplication/ProcessedMessageDeduplicationExperimentTests.cs`](ProcessedMessageDeduplication/ProcessedMessageDeduplicationExperimentTests.cs) |
| 18 | `http-order-async-trace-correlation-v1` | [`AsyncTraceCorrelation/AsyncTraceCorrelationExperimentTests.cs`](AsyncTraceCorrelation/AsyncTraceCorrelationExperimentTests.cs) |
| 19 | `eventbus-observation-baseline-v1` | [`EventBusObservationBaseline/EventBusObservationBaselineExperimentTests.cs`](EventBusObservationBaseline/EventBusObservationBaselineExperimentTests.cs) |
| 20 | `eventbus-publish-then-crash-v1` | [`EventBusPublishCrash/EventBusPublishCrashExperimentTests.cs`](EventBusPublishCrash/EventBusPublishCrashExperimentTests.cs) |

**Hard end:** Exp 20 closes the MCP/EventBus research branch. No Exp 21 in this line.

### MAF prototype spike (not a numbered experiment)

**Status:** wiring verified; live agent runs require a chat provider (OpenAI, Azure OpenAI, or Ollama).

[`MafMcpPrototype/MafMcpPrototypeTests.cs`](MafMcpPrototype/MafMcpPrototypeTests.cs) places a real [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) agent in front of the existing FeatureFusion `/mcp` endpoint (Aspire + `WebApplicationFactory`, unchanged `BuildingBlocks.Mcp`). It records ordered MCP tool-call sequences from `AgentResponse.Messages` and correlates them with in-process `mcp.tool` / Mediator / Npgsql spans plus outbox/handler evidence.

Run:

```bash
# Transport-only (no LLM key required)
dotnet test tests/Lab/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~Maf_mcp_transport_connects"

# Live agent — OpenAI / Azure
export OPENAI_API_KEY=...
export OPENAI_MODEL=gpt-4o-mini   # optional
dotnet test tests/Lab/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~Maf_agent_runs_goal"

# Live agent — local/self-hosted Ollama (OpenAI-compatible /v1)
export OLLAMA_BASE_URL=http://127.0.0.1:11434
export OLLAMA_MODEL=qwen3.5:4b-q4_K_M
dotnet test tests/Lab/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~Maf_agent_runs_goal"
```

Ollama is a Lab research convenience, not a FeatureFusion dependency. Choose a model that advertises `tools` capability. Streaming is unused.

This is research infrastructure only — not Exp 15 and not a BuildingBlock.

### Workstream status — HTTP Redis idempotency (Exp 3, 4, 12)

**COMPLETE — BuildingBlocks.Idempotency 1.0.0**

Reusable implementation: [`src/BuildingBlocks/Idempotency`](../../../../src/BuildingBlocks/Idempotency). Lab hosts the package on `POST /api/v2/Order/order`; these three experiments are the **evidence / provenance trail**, not unfinished extraction work. Package unit tests live under `tests/BuildingBlocks/Idempotency.Tests`. MCP `orders.create` idempotency (Exp 6) is a separate in-memory store and is **not** part of this BuildingBlock.

### Experiment 1 — HTTP pagination cursor abuse

- **Hypothesis / problem:** A deterministic “careless” client walking `GET /api/v2/products-page` produces observable cursor semantics (replay, stale reuse, tamper, malformed cursor) without claiming full pagination correctness.
- **Surface:** HTTP `GET /api/v2/products-page` → Mediator `GetProductsQuery` → PostgreSQL.
- **Primary behavior:** Clean walk yields 56 unique IDs across 8 pages; replay is stable; unsigned tamper shifts seek window; malformed cursor returns HTTP 400 before Mediator/Npgsql.
- **Limitation / non-goal:** Not a pagination correctness or performance suite. Host pagination signing key is not configured in the lab.

### Experiment 2 — MCP pagination cursor abuse

- **Hypothesis / problem:** Same careless pagination behaviors through MCP `products.list`, comparing the MCP envelope to Experiment 1.
- **Surface:** MCP `products.list` → same `GetProductsQuery` path as Exp 1.
- **Primary behavior:** Walk/replay/tamper parallels Exp 1; malformed cursor is MCP `isError` with Mediator but **no** Npgsql; POST `/mcp` transport TraceId ≠ `mcp.tool` TraceId.
- **Limitation / non-goal:** Does not re-prove keyset SQL correctness (Exp 1 already exercises the query). JSON artifact uses an anonymous payload (cosmetic difference from other experiments).

### Experiment 3 — HTTP Redis idempotency vs production (CacheVsProduction)

- **Role:** Originally characterized Lab HTTP idempotency; now **regression / provenance** that extracted `BuildingBlocks.Idempotency` preserves the intended miss/hit workflow (fingerprint **off**, Lab default).
- **Workstream:** COMPLETE — BuildingBlocks.Idempotency 1.0.0 (extraction proof set).
- **Hypothesis / problem:** Package filter (Redis `IDistributedCache`) separates cache replay from production execution on `POST /api/v2/Order/order`.
- **Surface:** HTTP order create → `[Idempotent(useLock: true)]` → `CreateOrderCommandHandler` (Mediator + catalog SaveChanges + outbox insert on miss).
- **Primary behavior:** Miss runs production (Mediator + Npgsql); hit replays with `X-Idempotent-Response`; same key + different body keeps original order (body not part of key when fingerprint off); new key runs production again. Replay body casing reflects package Newtonsoft cache serialization.
- **Limitation / non-goal:** Does not cover MCP `orders.create` (Exp 6). Does not assert async handler delivery (Exp 5). Does not prove concurrency lock races (Exp 4) or opt-in fingerprinting (Exp 12).

### Experiment 4 — HTTP idempotency concurrency (ConcurrencyRace)

- **Role:** Originally proved the Redis same-key concurrency problem and lock behavior; now **regression / provenance** for `RedisIdempotencyLock` + Processing/Completed coordination under concurrent clients.
- **Workstream:** COMPLETE — BuildingBlocks.Idempotency 1.0.0 (extraction proof set).
- **Hypothesis / problem:** Concurrent clients with the **same** `Idempotency-Key` coordinate through Redis lock/status; different keys run independently.
- **Surface:** Same HTTP order-create path as Exp 3 (`BuildingBlocks.Idempotency`).
- **Primary behavior:** Three concurrent same-key requests → exactly one production winner, one unique `orderId`; losers are cached replay, HTTP 409, or lock-failure HTTP 500 (as observed). Two different keys both run production.
- **Limitation / non-goal:** `StartTogether` gate is local to this experiment. Does not prove MCP or outbox semantics. Does not prove fingerprinting (Exp 12). ProcessingTtl lease overlap while a handler is still in flight is **Exp 15**.

### Experiment 5 — HTTP order create → outbox → handler

- **Hypothesis / problem:** Cache-miss order create eventually delivers exactly one `OrderCreatedIntegrationEvent` to the consumer; idempotent HTTP replay does not deliver a second event.
- **Surface:** HTTP order create → outbox insert → `OutBoxWorker` → RabbitMQ → `OrderCreatedIntegrationEventHandler`.
- **Primary behavior:** Baseline HTTP success + Mediator/Npgsql; bounded wait until one handler observation; replay is cached HTTP with no Mediator/Npgsql and no second event.
- **Limitation / non-goal:** **`ProcessedEvents` is test observation infrastructure** (decorator on the real handler), not production telemetry. Proves **eventual handler observation**, not direct proof that a specific `outbox_messages` row transitioned pending→processed or that RabbitMQ was the only publish path.

### Experiment 6 — MCP orders.create confirmation + idempotency

- **Hypothesis / problem:** MCP `orders.create` has its own confirmation gate and `MemoryIdempotencyStore` replay semantics, distinct from HTTP Redis idempotency (Exp 3).
- **Surface:** MCP `orders.create` → `McpInvoker` → `CreateOrderCommand` (same handler as HTTP on cache miss).
- **Primary behavior:** Unconfirmed call fails with `ConfirmationRequired` before `mcp.tool`; confirmed miss runs Mediator + Npgsql; same-key replay returns cached result with **no new** `mcp.tool` span; same key + different quantity replays original; new key runs fresh production.
- **Limitation / non-goal:** Does not re-prove HTTP Redis idempotency (Exp 3) or outbox/async delivery (Exp 5). Does not use `ProcessedEvents`.

### Experiment 7 — duplicate integration-event delivery / consumer deduplication

- **Hypothesis / problem:** When the same `OrderCreatedIntegrationEvent` identity (`IntegrationEvent.Id` / RabbitMQ `MessageId`) is delivered twice through the real consumer, does the handler run once or twice?
- **Surface:** `IEventBus.PublishDirect` → RabbitMQ → `MessageProcessor` → `inbox_messages` → `OrderCreatedIntegrationEventHandler` (decorated by `TestEventHandlerDecorator` in the fixture).
- **Observed behavior:** Baseline runs the handler once and stores one `inbox_messages` row. Duplicate publish with the **same** event identity after successful processing reaches the consumer again (`EventBus` `ProcessMessage` spans) but does **not** invoke the handler a second time — suppressed by `inbox.IsDuplicateAsync` when `IsProcessed=true`. Control publish with a **new** `IntegrationEvent.Id` but the same `OrderId`/`Total` runs the handler again. `EnableDeduplication=false` in the lab; `processed_messages` is unused; inbox completion deduplication still applies.
- **Limitation / non-goal:** Does not prove outbox publish path (Exp 5). Does not enable or exercise `MessageDeduplicationService` / `EnableDeduplication=true` — that is **Exp 17**. Handler has no durable business side effect beyond logging and an in-memory list — `ProcessedEvents` and `inbox_messages` are the primary evidence. Does not simulate RabbitMQ redelivery flags (`redelivered=true`); uses a second explicit publish with preserved message identity.

### Experiment 8 — HTTP order outbox lifecycle fingerprint

- **Hypothesis / problem:** Exp 5 proves eventual handler delivery but not `outbox_messages` persistence. Does a cache-miss HTTP order insert one outbox row, does `OutBoxWorker` mark it processed after publish, and does idempotent replay avoid a second row?
- **Surface:** `POST /api/v2/Order/order` (miss) → `CreateOrderCommandHandler` → `IntegrationEventService` → transactional `outbox_messages` → `OutBoxWorker` → `PublishDirect` → RabbitMQ → inbox → handler.
- **Observed behavior:** One outbox row per order (`Id == IntegrationEvent.Id` ≠ `OrderId`); worker sets `Status=Processed` with `ProcessedAt`/`CompletedAt` after `PublishDirect`; one handler observation correlated by `IntegrationEvent.Id`; idempotent replay returns cached HTTP with no Mediator/Npgsql and no additional outbox row; new idempotency key creates a separate order and outbox row (control).
- **Evidence:** `CatalogDbContext.OutboxMessages` queries (production persistence); `ProcessedEvents` (test decorator); optional `inbox_messages` correlation; Mediator/Npgsql spans on miss vs replay.
- **Limitation / non-goal:** Does not claim exactly-once RabbitMQ delivery or crash consistency if the worker fails between publish and mark-processed. May observe the row already processed immediately after HTTP if the worker poll wins the race (`ProcessedAt==null` is the worker’s pending selector). Does not enable `EnableDeduplication=true`. Does not re-prove consumer duplicate suppression (Exp 7).

### Experiment 9 — EventBus failure outcome / retry-DLQ behavior

- **Hypothesis / problem (discovery-oriented):** When a failing integration event is published, what consumer, inbox, retry-header, ACK/NACK, and DLQ behavior actually occurs? Does **not** assume `RetryCount=3` produces three handler attempts.
- **Surface:** `IEventBus.PublishDirect` → RabbitMQ (`feature_fusion` / `feature_fusion_dlq`) → `EventBus.MessagetHandler` → `MessageProcessor` → inbox → handler; control uses invalid JSON (same pattern as `RabbitMQEventBusTests.Should_Not_Requeue_Invalid_Messages`).
- **Observed behavior (lab, after fixture fix):** `FailingIntegrationEvent` **reaches the handler once**, then **dead-letters** (`ProcessMessage` status `failed`) with inbox **not** marked processed and subscriber **Failed**. Permanent-failure control (invalid JSON) still dead-letters immediately without inbox. Prior runs with mis-registered handlers showed pre-handler DLQ (zero handler invocations); that was a **test fixture issue**, not production DLQ semantics for handler throws.
- **Limitation / non-goal:** Characterizes mixed pre-handler control + legacy failing-event phase. **Experiment 11** is the dedicated handler-throw characterization (plain, `TransientException`, `BusinessException`). Does not change `RetryCount`, enable deduplication, or claim exactly-once/at-least-once semantics.

### Experiment 10 — MCP confirmed order → outbox → handler parity

- **Hypothesis / problem:** After a confirmed MCP `orders.create` cache miss, does the real workflow persist `outbox_messages` and deliver `OrderCreatedIntegrationEvent` through worker → RabbitMQ → inbox → handler as HTTP (Exp 8)?
- **Surface:** MCP `orders.create` (confirmed) → `McpInvoker` → `CreateOrderCommandHandler` → `IntegrationEventService` → outbox → `OutBoxWorker` → consumer → handler.
- **Observed behavior:** Confirmed miss runs `mcp.tool` + Mediator + Npgsql; one outbox row (`Id == IntegrationEvent.Id` ≠ `OrderId`); worker marks processed; inbox row + `ProcessedEvents` handler observation (ProcessMessage Activity may be absent in-process for outbox-triggered delivery); MCP idempotency replay returns same order with no new tool/Mediator/Npgsql/outbox/handler work; new idempotency key creates independent order/outbox/handler path (control).
- **Evidence:** MCP tool spans, `outbox_messages`, `inbox_messages`, `EventBus` `ProcessMessage` Activities when captured, `ProcessedEvents` (test decorator).
- **Limitation / non-goal:** Proves observed pipeline parity with Exp 8, not merely shared handler type. Does not claim exactly-once RabbitMQ delivery or worker crash consistency. Intermediate pending outbox state may be skipped if worker poll is fast. In-process `ProcessMessage` Activity capture is best-effort for async outbox worker delivery — consumer path is also inferred from inbox + handler evidence. Does not re-prove MCP confirmation/idempotency mechanics (Exp 6) or HTTP Redis idempotency (Exp 3).

### Experiment 11 — handler-throw failure outcome

- **Hypothesis / problem:** When a deserializable event reaches a registered handler that throws, what does `MessageProcessor` return and does `MessagetHandler` ACK/NACK/requeue/DLQ? After Phase 6, does `RetryCount` bound transient handler executions via `inbox_subscribers.Attempts`?
- **Surface:** `PublishDirect` → consumer → inbox → `DispatchToHandlers` for plain `Exception`, `TransientException`, `BusinessException`, and once-transient-then-succeed test handlers.
- **Observed behavior (lab):** Plain `Exception` and `BusinessException` → handler once → **DLQ**, `Attempts=1`. Always-transient → **exactly RetryCount=3** handler executions (`retrying` then `failed`) → **DLQ**, `Attempts=3`, inbox not processed. Once-transient-then-succeed → 2 executions → **ACK**, inbox processed. Broker `x-retry-count` / `message.retry_count` stays **0** (not the DB attempt budget).
- **Evidence:** Test-only handler counters, `inbox_subscribers.Attempts`, queue/DLQ depths, `EventBus` `ProcessMessage` Activities, `inbox_messages`.
- **Limitation / non-goal:** Does not increment `x-retry-count` or apply `CalculateRetryDelay`. `message.retry_count` remains the broker header.

### Experiment 12 — HTTP idempotency request fingerprint (opt-in)

- **Role:** Proves the **opt-in request fingerprint** capability added in richer BuildingBlocks.Idempotency v1 (not part of the original extraction proof set). Remains a **regression / proof gate** for that feature.
- **Workstream:** COMPLETE — BuildingBlocks.Idempotency 1.0.0 (richer v1 evidence).
- **Hypothesis / problem:** With `EnableRequestFingerprint=true`, same key + same body replays; same key + different body conflicts (422) without production.
- **Surface:** Same order-create path as Exp 3; fingerprint enabled via test-host `PostConfigure<IdempotencyOptions>` only (Lab app default stays off).
- **Primary behavior:** Miss runs production; same-body replay sets `X-Idempotent-Response`; different quantity → HTTP 422, no Mediator.
- **Limitation / non-goal:** Does not change Lab default (fingerprint off — Exp 3 remains the fingerprint-off compatibility gate). Does not re-prove Exp 4 concurrency.

### Experiment 13 — MCP agent write retry with regenerated idempotency keys

- **Role:** Characterizes **unreliable agent client behavior** — one logical write intent retried with new idempotency keys after a successful result (simulated context loss).
- **Hypothesis / problem:** If an agent retries the same logical `orders.create` with regenerated keys (K2, K3) after K1 succeeded, how much business-side amplification occurs (orders, Mediator/Npgsql, outbox, handler observations)?
- **Surface:** MCP `orders.create` (`confirmed=true`) → `MemoryIdempotencyStore` → `CreateOrderCommandHandler` → outbox → async handler (`ProcessedEvents` test decorator).
- **Primary behavior:** K1 miss → production; K1 replay → cached replay (no new tool/Mediator/Npgsql); K2/K3 with new keys → additional production paths. Measures distinct `orderId`s, outbox rows, and downstream handler counts per order.
- **Relationship:** Extends Exp 6 (same-key replay / fresh-key control) with **retry-after-success** agent semantics. Uses Exp 10 outbox/handler observation patterns. **Not** HTTP `BuildingBlocks.Idempotency` (Exp 3/4/12).
- **Limitation / non-goal:** Does not claim idempotency is broken when keys are regenerated — deduplication is keyed by idempotency key by design. Does not prove concurrent same-key MCP calls (deferred). Does not change MCP or package code.

### Experiment 14 — Concurrent Same-Key MCP write (McpConcurrentSameKey)

- **Role:** Characterizes unreliable/agentic client behavior under **in-flight** concurrency: the same MCP write is submitted concurrently with identical arguments and a shared idempotency key.
- **Hypothesis / problem:** If multiple concurrent agent/tool calls represent the same write and share an idempotency key, does MCP preserve exactly one production outcome (one order, one outbox row, one downstream handler observation), with other callers replaying?
- **Surface:** MCP `orders.create` (`confirmed=true`) → `McpInvoker` → in-process `MemoryIdempotencyStore` in-flight gating → CreateOrderCommandHandler → outbox → `OutBoxWorker` → RabbitMQ → inbox/handler (`ProcessedEvents` test decorator).
- **Observed behavior (asserted by this experiment’s invariants):** All 3 parallel calls succeeded; they all returned a single distinct `orderId`; the capture shows exactly **one** `mcp.tool` production execution (tool trace count); and production evidence is `outbox_messages=1` with downstream `ProcessedEvents` for the winner order `=1`.
- **Relationship / compare:** Exp 4 proves HTTP/Redis concurrency; Exp 6 proves same-key replay; Exp 13 proves regenerated-key amplification. Exp 14 completes the matrix for concurrent same-key MCP calls.
- **Limitation / non-goal:** Does not test regenerated-key behavior (Exp 13), HTTP/Redis semantics (Exp 4), or pagination misuse/cursor abuse (Exp 1/2).

### Experiment 15 — HTTP idempotency ProcessingTtl lease overlap

- **Role:** Closes the lease-expiry gap deferred by Exp 4 / package docs: what happens when the first same-key request stays in `Processing` longer than `ProcessingTtl`.
- **Hypothesis / problem:** After the Processing lease expires, a second client with the same `Idempotency-Key` may run production while the first handler is still in flight → duplicate orders/outbox.
- **Surface:** HTTP `POST /api/v2/Order/order` → `BuildingBlocks.Idempotency` (`useLock: true`) → Mediator `CreateOrderCommand` → outbox. Test host only: short `ProcessingTtl` via `PostConfigure` + test-only gated handler wrapper (production handler unchanged).
- **Primary behavior (characterized by this experiment):** Probe while first held → HTTP 409; after lease expiry → second request executes production; both complete with distinct `orderId`s and outbox rows when the packaged lease tradeoff admits overlap.
- **Limitation / non-goal:** Does not add lease renewal or change Lab/package `ProcessingTtl` defaults. Does not re-prove Exp 3/4/12. Gate is test observation infrastructure only.

### Experiment 16 — MCP tool-storm → IMcpRateLimiter

- **Role:** Complements Exp 13 (distinct-key amplification): when idempotency cannot collapse a write storm, does `IMcpRateLimiter` bound execution at the MCP boundary?
- **Hypothesis / problem:** A burst of confirmed `orders.create` calls with **distinct** idempotency keys is rate-limited after a fixed permit budget; surplus returns `McpErrorCode.RateLimited` before Mediator/Npgsql/outbox.
- **Surface:** MCP `orders.create` → `McpInvoker` (`IMcpRateLimiter` before confirmation/idempotency/`InvokeCore`) → dispatcher only for allowed calls. Test host replaces `NoOpRateLimiter` with a fixed-permit limiter; Lab default stays no-op.
- **Primary behavior:** With budget N and storm size M (M&gt;N), exactly N production paths (mcp.tool + Mediator + Npgsql + order/outbox); M−N errors with `RateLimited` and no tool/Mediator/Npgsql spans.
- **Limitation / non-goal:** Not an idempotency experiment. Does not ship a rate-limiter NuGet or change package limiter code. Scope of the test limiter is per-tool fixed permits (package interface is implementation-defined). No MAF/LLM.

### Experiment 17 — EnableDeduplication=true / processed_messages

- **Role:** Completes the consumer dedup family Exp 7 left open: when `EnableDeduplication=true`, does `processed_messages` record successful deliveries and suppress a same-`IntegrationEvent.Id` redelivery before handler dispatch?
- **Hypothesis / problem:** First `PublishDirect` of an identity runs the handler and inserts `processed_messages`; second publish with the **same** id reaches `ProcessMessage` again but returns Success from the `processed_messages` check before `DispatchToHandlers` — exactly one handler observation.
- **Surface:** `IEventBus.PublishDirect` → RabbitMQ → `MessageProcessor.ProcessMessageAsync(deduplication:true)`. Test flips `EventBusOptions.EnableDeduplication` on the shared options instance for this test only (restored in dispose); fixture default stays false.
- **Primary behavior:** After baseline: inbox processed + `processed_messages=1` + handler=1. After duplicate: ProcessMessage attempts increase, handler count stays 1, still one inbox row and one `processed_messages` row. Control new id → second handler + its own `processed_messages` row.
- **Limitation / non-goal:** Does not simulate outbox publish-then-crash. Does not change production EventBus. After a successful first delivery both inbox and `processed_messages` would suppress; the code checks `processed_messages` first when the flag is on. Exp 7 remains the inbox-only (flag off) gate.

### Experiment 18 — Async trace correlation (HTTP → outbox → consumer)

- **Role:** Characterizes whether W3C trace context crosses the async messaging boundary, or whether only business ids remain correlatable.
- **Hypothesis / problem:** After HTTP order create → outbox → RabbitMQ → consumer, does `EventBus` `ProcessMessage` share the originating TraceId / parent / Activity links, or start a separate trace?
- **Surface:** `POST /api/v2/Order/order` (Lab `traceparent` like Exp 8) → Mediator → outbox → `OutBoxWorker` → `PublishDirect` → consumer `ProcessMessage`.
- **Observed production instrumentation:** No `traceparent` on RabbitMQ headers; `OutBoxWorker`/`PublishDirect` emit no spans; `ProcessMessage` starts a root `EventBus` Activity tagged with `message.id`. Correlation across the boundary is via `IntegrationEvent.Id` / `OrderId`.
- **Limitation / non-goal:** Does not add propagation or change Telemetry/EventBus. Documents the gap if separate consumer traces are observed.

### Experiment 19 — EventBus observation baseline

- **Role:** Proves the Lab-only EventBus observation seam on the happy path (no faults).
- **Hypothesis / problem:** Can we journal Observed stages for outbox → publish → processor → handler, and label Ack as Inferred?
- **Surface:** HTTP order create + `IEventBusLabHook` (disarmed) + `LabMessageProcessorDecorator` + outbox/inbox/`ProcessedEvents`.
- **Primary behavior:** AfterPublishBeforeOutboxMark, ProcessorEntered/Completed, handler once, inbox processed; `message.status=processed` inferred near Ack.
- **Limitation / non-goal:** Dedup not exercised (see Exp 7/17). No W3C. No Scenario engine.

### Experiment 20 — Publish-then-crash (fault B) — **branch end**

- **Role:** Final EventBus research experiment. Characterizes crash after broker publish before outbox MarkProcessed.
- **Hypothesis / problem:** Outbox stays pending; later poll may republish; inbox may still run the handler once for the same `IntegrationEvent.Id`.
- **Surface:** One-shot Lab fault B via `EventBusLabFaultController.ArmCrashAfterPublishOnceForOrderId`.
- **Limitation / non-goal:** Does not fix duplicate-publish risk. Does not add W3C. No Exp 21 in this branch.

---

## Established experiment convention

Recurring pattern (not a base class):

```
hypothesis (XML docs)
  → labeled invocations (behavior string per call)
  → real production path (HTTP or MCP through AspireFixture)
  → telemetry / evidence capture
  → JSON artifact (_output.WriteLine)
  → FluentAssertions on invariants
```

### Why one `[Fact]`

Each experiment is a single end-to-end journal: one hypothesis, one ordered sequence of invocations, one artifact, one assertion block. Splitting phases into multiple facts would fragment evidence and complicate fixture state (e.g. idempotency keys, `ProcessedEvents`).

### Why labeled behaviors

Every invocation carries a `behavior` label (`Walk`, `MissPopulate`, `SameKeyReplay`, `BaselineProduction`, …). Labels make artifacts readable and assertions traceable without a DSL or runner type.

### Why experiment-specific call records

Call shape differs by domain (pagination cursors, MCP `isError`, idempotency headers, concurrency roles). Shared call types would force false uniformity.

### Why artifact before assertions

The JSON artifact is the experiment’s audit trail. It is emitted to `ITestOutputHelper` **before** assertions so failed runs still leave useful output in test logs.

Typical artifact envelope (exact property naming varies by experiment):

- `name`, `startedUtc`, `gitSha`, optional `environment` / `configuration`
- `calls[]` — per-invocation evidence
- `observations` — roll-up metrics
- `notes[]` — human-readable interpretation

---

## Evidence model

### In-process telemetry (production Activities, test-process capture)

| Piece | Location | Role |
|-------|----------|------|
| `InProcessActivityCapture` | [`Infrastructure/Telemetry/`](../Infrastructure/Telemetry/InProcessActivityCapture.cs) | `ActivityListener` in the test process; collects stopped Activities while FeatureFusion runs in-process via WAF. `Clear()` drops spans so a later independent run in the same test does not inherit cumulative counts. |
| `CapturedActivity` | [`Infrastructure/Telemetry/CapturedActivity.cs`](../Infrastructure/Telemetry/CapturedActivity.cs) | Snapshot of one span (source, display name, trace/span/parent ids, tags, duration, Activity links) |
| `LabTrace` | [`Infrastructure/Telemetry/LabTrace.cs`](../Infrastructure/Telemetry/LabTrace.cs) | W3C `traceparent` helpers and generic tag predicates |

**`traceparent` propagation:** HTTP experiments inject `LabTrace.NewTraceParent()` + `FormatTraceParent()` on each request. MCP Experiment 2 sets transport `traceparent` on the shared `HttpClient`; tool-level traces are discovered separately (Exp 2, Exp 6).

**Narrow local predicates:** Experiments 2–6 often use source-only `IsMediator` / `IsNpgsql` (`BuildingBlocks.Mediator`, `Npgsql`) for stable span counts. `LabTrace` also offers broader tag-based predicates; experiments keep local narrow helpers when counting semantics must not drift.

### Test observation infrastructure (not production telemetry)

| Piece | Location | Role |
|-------|----------|------|
| `AspireFixture.ProcessedEvents` | [`AspireFixture.cs`](../Aspire/AspireFixture.cs) | List populated by `TestEventHandlerDecorator` wrapping `OrderCreatedIntegrationEventHandler` |
| `Wait.UntilAsync` | [`Infrastructure/Async/Wait.cs`](../Infrastructure/Async/Wait.cs) | Bounded condition polling (100 ms interval); used for eventual handler delivery (Exp 5) and EventBus tests |

**`ProcessedEvents`:** Proves the **decorated handler ran** in the integration test host after HTTP/outbox/RabbitMQ processing. It is **not** how operators observe production. Exp 5 documents this explicitly in XML docs, configuration notes, and artifact `notes`.

**Production telemetry** (OTLP, SigNoz, etc.) is configured in the lab app but is **not** required for experiments. In-process capture substitutes for collector-based proof in the test process.

---

## Shared vs local infrastructure

### Intentionally shared

| Helper | Path | Used for |
|--------|------|----------|
| Telemetry capture | `Infrastructure/Telemetry/*` | All experiments + TraceEvidence |
| Async wait | `Infrastructure/Async/Wait.cs` | Exp 5, EventBus tests |
| MCP client factory | `Infrastructure/Mcp/LabMcpClient.cs` | Exp 2, Exp 6, MCP smoke, TraceEvidence |
| Run metadata | `Infrastructure/Reporting/LabRunInfo.cs` | Exp 6 today (`ReadGitSha()`); older experiments still use local copies |

### Intentionally local

- Call record types and observation roll-ups
- Invocation orchestration (walk loops, cache phases, concurrency gates)
- Domain clients (`CarelessPaginationClient` in Exp 1)
- Experiment-specific assertion sets
- Concurrency gate (`StartTogether` in Exp 4 only)
- Artifact serialization choices (source-generated JSON in Exp 1, records elsewhere, anonymous object in Exp 2)

Extract a helper only when semantics are **stable across experiments**. Duplication that reflects different hypotheses should stay local.

---

## Anti-patterns / non-goals

The Lab **does not** currently use:

- Scenario / Actor models
- Experiment DSL or invocation language
- `ExperimentEngine` or generic invocation runner
- Shared experiment base class
- Universal artifact schema or shared `IExperimentResult` type

These are intentionally avoided. Fourteen numbered experiments share a **convention**, not a framework. Premature abstraction would force uniform call shapes and hide domain-specific evidence. Revisit only if multiple future experiments demonstrate stable semantics that genuinely require shared types.

---

## Current limitations

- **Exp 5 — handler vs outbox proof:** Superseded for outbox persistence by Exp 8; Exp 5 still documents handler-only observation without `outbox_messages` fingerprinting.
- **HTTP vs MCP idempotency:** Exp 3/4/12 (Redis / `BuildingBlocks.Idempotency`) and Exp 6 (`MemoryIdempotencyStore` / `McpInvoker`) are different stores, gates, and replay signals (`X-Idempotent-Response` vs absence of new `mcp.tool` span). HTTP Redis workstream is COMPLETE; Exp 6 is MCP-only.
- **Gateway / rate limiting:** YARP + Memcached tests live in `FeatureFusion.ApiGateway.Tests`, not the Aspire `IntegrationTests` experiment host.
- **Inbox deduplication:** Experiment 7 exercises duplicate delivery with `EnableDeduplication=false` (inbox completion). **Exp 17** exercises `EnableDeduplication=true` / `processed_messages`.
- **Recommendation cache middleware, feature-flag paths, SigNoz/OTLP in tests:** Present in the lab app but not covered by Experiments 1–18.
- **Test isolation:** `[Collection(AspireCollection.Name)]` with `DisableTestParallelization = true`. Experiments that use `ProcessedEvents` should clear or scope observations (Exp 5 clears at start).
- **Minor artifact inconsistency:** Exp 2 captures `startedUtc` at artifact build time; others capture at test start. JSON property casing differs (anonymous camelCase vs record PascalCase).

---

## How to add a new experiment

1. **Inspect the real production workflow** — routes, MCP tools, handlers, filters, stores. Do not assume behavior from prior experiments.
2. **Formulate one falsifiable hypothesis** — document in the test class XML summary with explicit non-goals.
3. **Add a folder under `Experiments/`** — one test class, one `[Fact]`, `[Collection(AspireCollection.Name)]`.
4. **Reuse shared observation infrastructure** — `InProcessActivityCapture`, `LabTrace`, `Wait.UntilAsync`, `LabMcpClient` when applicable.
5. **Keep domain semantics local** — call records, invocation sequence, helpers, assertions.
6. **Label every invocation** with a `behavior` string.
7. **Emit JSON artifact before assertions** — include `name`, `startedUtc`, `gitSha`, `calls`, `observations`, `notes`.
8. **Assert application-visible behavior first** — HTTP status, MCP `isError`, response bodies, headers.
9. **Assert collaborator evidence second** — Mediator/Npgsql span counts, `ProcessedEvents`, tool trace presence.
10. **Avoid fixed sleeps** for eventual behavior — use `Wait.UntilAsync` with a bounded timeout.
11. **Do not modify production code** unless explicitly requested for a separate task.
12. **Validate:**

```bash
dotnet test tests/Lab/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~YourExperimentTests" \
  --verbosity normal

dotnet test tests/Lab/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~FeatureFusionTraceEvidenceTests" \
  --verbosity normal

dotnet test tests/Lab/IntegrationTests/IntegrationTests.csproj \
  --filter "FullyQualifiedName~PaginationAbuseExperimentTests|FullyQualifiedName~McpPaginationAbuseExperimentTests|FullyQualifiedName~CacheVsProductionExperimentTests|FullyQualifiedName~ConcurrencyRaceExperimentTests|FullyQualifiedName~OutboxDeliveryExperimentTests|FullyQualifiedName~McpOrderIdempotencyExperimentTests|FullyQualifiedName~DuplicateDeliveryExperimentTests|FullyQualifiedName~OutboxLifecycleExperimentTests|FullyQualifiedName~EventBusFailureExperimentTests|FullyQualifiedName~McpOrderOutboxExperimentTests|FullyQualifiedName~HandlerThrowFailureExperimentTests|FullyQualifiedName~IdempotencyFingerprintExperimentTests|FullyQualifiedName~McpAgentKeyRegenerationExperimentTests|FullyQualifiedName~McpConcurrentSameKeyExperimentTests|FullyQualifiedName~IdempotencyProcessingLeaseExperimentTests|FullyQualifiedName~McpToolStormRateLimitExperimentTests|FullyQualifiedName~ProcessedMessageDeduplicationExperimentTests|FullyQualifiedName~AsyncTraceCorrelationExperimentTests|FullyQualifiedName~YourExperimentTests" \
  --verbosity normal
```

Note: `FullyQualifiedName~PaginationAbuseExperimentTests` matches **both** Exp 1 and Exp 2 class names by substring; use exact class names when filtering a single experiment.

---

## Folder layout

```
Experiments/
  README.md                          ← this file
  PaginationAbuse/                   ← Exp 1 (HTTP), Exp 2 (MCP)
  CacheVsProduction/                 ← Exp 3 (HTTP Redis idempotency provenance)
  Concurrency/                       ← Exp 4 (Redis lock concurrency provenance)
  OutboxDelivery/                    ← Exp 5
  McpOrderIdempotency/               ← Exp 6
  DuplicateDelivery/                 ← Exp 7
  OutboxLifecycle/                   ← Exp 8
  EventBusFailure/                   ← Exp 9
  McpOrderOutbox/                    ← Exp 10
  HandlerThrowFailure/               ← Exp 11
  IdempotencyFingerprint/            ← Exp 12 (opt-in fingerprint, richer v1)
  McpAgentKeyRegeneration/           ← Exp 13 (agent regenerated idempotency keys)
  McpConcurrentSameKey/             ← Exp 14 (concurrent same-key MCP idempotency)
  IdempotencyProcessingLease/       ← Exp 15 (HTTP ProcessingTtl lease overlap)
  McpToolStormRateLimit/            ← Exp 16 (MCP distinct-key storm + IMcpRateLimiter)
  ProcessedMessageDeduplication/    ← Exp 17 (EnableDeduplication + processed_messages)
  AsyncTraceCorrelation/            ← Exp 18 (HTTP→outbox→consumer TraceId correlation)

Infrastructure/                      ← shared observation helpers (not experiments)
  Telemetry/
  Async/
  Mcp/
  Reporting/
```
