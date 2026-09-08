# Capability admission (lab proof)

Application-owned **Defer-before-Send** for capability `orders.create`. This is a FeatureFusion lab vertical slice — **not** a NuGet BuildingBlock yet.

## Why it exists

HTTP and MCP already share `CreateOrderCommand` / `CreateOrderCommandHandler`, but write safety lived on each surface (`Idempotency-Key`, MCP `confirmed` + memory keys). Agents (and careless clients) can satisfy same-request MCP confirmation. Admission puts a durable decision **in front of** `ISender.Send`.

## Why Defer is before Send

```
HTTP / MCP → Admit(orders.create) → Allow | Deny | Defer
Allow  → existing Send → existing handler
Defer  → persist intent ticket → return Pending (no Send, no Order, no Outbox)
Release (trusted HTTP) → claim ticket → existing Send → existing handler
```

If admission ran inside the handler or after outbox insert, the business effect would already exist.

## Why MCP `confirmed=true` is insufficient

`RequireConfirmation` is a same-request JSON flag. The MAF prototype instructs the model to set it. It must **not** release a deferred ticket.

## Why EventBus is not the mechanism

Outbox → RabbitMQ → inbox runs **after** the order is created. Messaging is post-effect fan-out, not pre-execution admission.

## Why MAF is a future consumer

Microsoft Agent Framework already calls `/mcp` in a test prototype. Later it can receive a Pending ticket from `orders.create` and must not call the HTTP release endpoint. No MAF types belong in admission.

## Ticket lifecycle

| Status | Meaning |
|--------|---------|
| Pending | Intent stored; `Send` has not run |
| Released | Atomic claim succeeded; handler may have run |
| Expired | Past `ExpiresAt`; cannot release |

**Request identity:** HTTP `Idempotency-Key` / MCP `idempotencyKey` + capability id. Same key while Pending returns the **same** `TicketId`. Different intent hash → 422 at admission (when Admit runs). Surface idempotency caches may replay the first Pending without re-entering Admit (fingerprint-off HTTP / MCP memory store) — characterized in tests.

## Exactly-once limitation

Release **atomically** transitions Pending → Released (one winner under concurrency). Then `Send` runs. A crash after claim and before/during handler may leave a Released ticket without an order. Ticket release is **not** mathematical exactly-once execution. Do not pretend otherwise.

## Configuration

```json
"CapabilityAdmission": {
  "DeferredCapabilities": [ "orders.create" ],
  "TicketTtl": "01:00:00"
}
```

Empty `DeferredCapabilities` → Allow (existing smoke / Exp 1–20 behavior). AspireFixture clears Defer so legacy experiments stay green; admission product tests re-enable it.

## Endpoints

- `POST /api/v1/Order/order` — Admit then Allow/Defer
- `POST /api/v1/admission/tickets/{ticketId}/release` — trusted release (`X-Released-By` optional)
- `GET /api/v1/admission/tickets/{ticketId}` — inspect
