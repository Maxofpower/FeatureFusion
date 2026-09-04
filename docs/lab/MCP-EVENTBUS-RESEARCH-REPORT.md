# MCP / EventBus research branch — final report

**Status:** Closed after Exp 20. Do not add Exp 21 in this line.  
**Scope:** FeatureFusion Lab experiments 1–20 + MAF prototype (non-numbered).  
**Date:** 2026-09-04

This document ends the MCP/EventBus research branch and marks the transition point to FeatureFusion **capability development** (capability choice is a separate next step).

---

## 1. What Exp 1–20 established

| # | Finding (short) |
|---|-----------------|
| 1–2 | Careless keyset pagination clients (HTTP/MCP) are observable and abusable |
| 3–4, 12, 15 | HTTP Redis idempotency: miss/hit, concurrency, fingerprint, ProcessingTtl lease overlap |
| 5, 8, 10 | Outbox → worker → RabbitMQ → inbox → handler works for HTTP and MCP |
| 6, 13, 14, 16 | MCP write semantics: confirm+replay, key regeneration amplifies, same-key concurrency safe, rate limit bounds storms |
| 7, 17 | Duplicate delivery: inbox dedup (flag off) vs `processed_messages` (flag on) |
| 9, 11 | Consumer failure → retry / DLQ / handler-throw outcomes characterized |
| 18 | **W3C Trace Context does not cross RabbitMQ**; correlate by `IntegrationEvent.Id` / `OrderId` |
| 19 | Lab observation seam journals happy-path stages (Observed vs Inferred) |
| 20 | Publish-then-crash (after broker publish / before outbox MarkProcessed) leaves outbox pending, may republish; inbox can still keep handler once |

---

## 2. Hypotheses confirmed

- HTTP Idempotency-Key + Redis is a reusable, shippable contract → **BuildingBlocks.Idempotency**.
- MCP tools can share Mediator handlers with HTTP when confirmation/idempotency are explicit.
- `IMcpRateLimiter` bounds distinct-key tool storms before invoke.
- Outbox/inbox pipeline delivers `OrderCreated` end-to-end under Aspire.
- Inbox (and optional `processed_messages`) suppress duplicate **handler** work for the same message identity.
- Agent regenerated MCP keys amplify business side effects (not an idempotency bug).
- Lab seam can observe publish-before-mark and processor enter/exit without changing default semantics when faults are disarmed.
- Crash-after-publish leaves outbox pending and risks a second `PublishDirect` (characterized, not fixed).

---

## 3. Hypotheses rejected / falsified

- “W3C automatically correlates async consumer work” — **rejected** (Exp 18).
- “Global `ProcessedEvents.Count` is experiment-owned” — **rejected** (Exp 13 flake; scoped by OrderId).
- “Same logical agent intent is safe under new MCP keys” — **rejected as safety claim**; amplification is expected under the key contract (Exp 13).
- “Processing lease covers full handler duration by default” — **rejected** (Exp 15 lease overlap).

---

## 4. Real production concerns

1. **Publish-then-crash / MarkProcessed gap** (Exp 8 named; Exp 20 characterized): broker may receive a message while outbox stays pending → republish risk.
2. **No W3C across RabbitMQ** (Exp 18): ops must use business ids for async correlation.
3. **MCP idempotency ≠ HTTP Redis idempotency**: agents that regenerate keys create real duplicate orders.
4. **ProcessingTtl** without renewal allows overlapping production under the same Idempotency-Key.
5. Consumer retry/DLQ semantics must be measured, not assumed from `RetryCount` alone (Exp 9/11).

---

## 5. Reusable BuildingBlock potential

| Already shipped / justified | Not justified as NuGet |
|-----------------------------|-------------------------|
| Mediator, Mcp, Idempotency, Pagination.EF, Telemetry, Aspire.Hosting.SigNoz | EventBus (lab-only) |
| | EventBus Lab seam / journal / fault controller |
| | Scenario / ExperimentEngine |
| | Fixed-permit MCP rate limiter test double |
| | Lease-hold handler gates |

---

## 6. Interesting but NOT worth implementing (now)

- Full W3C RabbitMQ propagation as a “quick fix” without product requirement.
- Generic EventBus framework or Scenario DSL.
- Packaging the Lab observation seam.
- Exhaustive fault matrix (delay, drop-before-ack, etc.) — Exp 20 is the deliberate end of this branch.
- Fixing publish-then-crash inside this research branch (product decision).

---

## 7. What MCP contributed

- Second surface over the same handlers (`orders.create`, `products.list`, …).
- Agent-relevant contracts: confirmation, memory idempotency, rate limiting.
- Evidence that agent clients need different failure modes than HTTP Redis keys (Exp 13/16).

---

## 8. What Aspire contributed

- Realistic local topology: Postgres, Redis, RabbitMQ, Memcached (+ SigNoz OTLP in AppHost).
- Shared `AspireFixture` for IntegrationTests without a parallel Testcontainers stack.
- Made outbox/inbox/DLQ experiments honest (real broker, real worker poll).

---

## 9. What telemetry contributed

- In-process `Activity` capture for Mediator / MCP / Npgsql / EventBus `ProcessMessage`.
- Proof that sync paths are correlatable; async path needs business ids (Exp 18).
- Lab journal complements (does not replace) OpenTelemetry for experiment evidence.

---

## 10. Lab infrastructure to retain

- `AspireFixture` + experiment convention (hypothesis → labeled calls → artifact → asserts).
- Phase 2 helpers: `HttpOrderCreate`, `OrderOutboxObserver`, `McpToolResults`, `LabTrace`, `LabRunInfo`.
- Lab EventBus seam: `IEventBusLabHook` (null no-op), journal, processor decorator, fault controller (Exp 19/20).
- Numbered Exp 1–20 catalog + `docs/lab/README.md` charter.
- Packable BuildingBlocks with proven extraction provenance.

---

## 11. Lab infrastructure that should remain disposable

- Per-experiment gates (lease hold, fixed-permit limiter).
- MAF/Ollama prototype wiring.
- Exp-specific JSON artifact shapes and call records.
- One-off fault arms beyond the closed branch’s Exp 20.

---

## 12. What should explicitly NOT be built (from this branch)

- Exp 21+ in the MCP/EventBus research line.
- Scenario/Actor/ExperimentEngine product.
- EventBus NuGet / generic messaging framework.
- Silent W3C propagation “to make traces pretty.”
- Production behavior changes solely to green tests.

---

## Product transition

**Research (this branch) ends at Exp 20.**

**Next step (separate session):** choose the next FeatureFusion **product capability** using this report — not by inventing another messaging experiment.

Transition criteria met when:

1. Evidence families 1–20 are documented and Exp 20 closed the EventBus seam question for this branch.
2. BuildingBlocks extraction rules remain intact.
3. Remaining EventBus gaps (crash recovery product fix, W3C) are logged as **product decisions**, not open-ended lab expansion.

Capability selection is intentionally deferred.
