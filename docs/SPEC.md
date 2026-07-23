# Spec: E-Commerce Order System (MVP)

**Source:** design and terminology (states, events, event flow)
are adapted from
[Designing an E-Commerce Order System](https://sadamkhan.spiralsync.com/blog/system-design/design-ecommerce-order-system).

## Objective

A demo distributed order system that shows a realistic choreography-based,
event-driven order placement flow, with correct handling of inventory and
payment consistency. Scoped as a learning/demo project — not a production
system.

**In scope (MVP):**
- Order Service
- Inventory Service
- Payment Service
- Fulfillment Service (shipping initiation only — no carrier integration)

**Explicitly out of scope for MVP:**
- Cart Service (client submits a fixed list of order items directly)
- Notification Service
- Analytics
- Search Service
- API Gateway (client calls Order Service directly)
- Sagas / saga orchestration (no central coordinator; plain choreography only)
- Locking strategies (pessimistic/optimistic concurrency control)
- TTL-based/expiring reservations
- Back-of-envelope capacity estimation, scalability/availability targets
- Authentication/authorization (no login, tokens, or identity verification;
  `UserId` is assumed to be supplied by the caller as-is)
- Transport-level failure handling (broker redelivery/retry policy, dead-
  lettering, poison-message handling) — only business-level failures (out
  of stock, payment decline) are addressed by this spec
- **Order cancellation** (customer-initiated cancellation of an already
  `RESERVED`/`CONFIRMED` order) — deferred; see note under Functional
  Requirements
- **Refunds** (the `PaymentRefunded` event/flow and the
  `DELIVERED → REFUND_PENDING → REFUNDED` return path) — deferred; no
  functional requirement or event flow triggers a refund in this MVP

**Success looks like:** a customer can place an order, the system reserves
inventory and processes payment via independent services reacting to events
(no central orchestrator), and the order reaches a terminal state consistent
with what actually happened to inventory and payment.

## Non-Functional Requirements

Consistency is the only NFR this spec addresses:

1. **No overselling** — inventory quantity must never go negative, and two
   orders must never both successfully reserve the same unit of stock.
2. **No uncharged confirmation / no chargeless loss** — an order is never
   marked `CONFIRMED` unless payment actually succeeded; a customer is
   never charged for an order that doesn't end up `CONFIRMED`.
3. **Idempotent event handling** — every service assumes at-least-once
   event delivery and must produce the same end state no matter how many
   times a given event is (re)delivered.
4. **Auditable state** — every order state transition is caused by exactly
   one event, and that event is durably recorded.

Everything else (latency, throughput, uptime %) is out of scope.

## Functional Requirements

1. **Place order** — customer submits an order (items + quantities,
   shipping address, payment method reference). System creates the order
   and begins the fulfillment pipeline.

   > **Known limitation:** there is no catalog/pricing service in scope.
   > `UnitPrice`/`TotalAmount` (see Data Model) are taken as submitted by
   > the client with the order request, unvalidated against any
   > independent price source. This is a known gap, accepted for this MVP.
2. **Reserve inventory** — system checks and reserves stock for every item
   in the order as an all-or-nothing operation.
3. **Process payment** — system charges the customer for the order once
   inventory is confirmed reserved.
4. **Confirm order** — once inventory is reserved and payment succeeds,
   the order is confirmed and handed to fulfillment.
5. **Track order status** — customer can query current order status at any
   time.
6. **Handle failures gracefully** — if inventory can't be reserved, or
   payment fails, the order is automatically moved to `CANCELLED` and any
   partial work (e.g. a reservation made before a later failure) is undone
   via a compensating event.

> **Out of scope for now:** customer-initiated order cancellation
> (cancelling an already `RESERVED`/`CONFIRMED` order). The state machine
> still allows the article's failure-driven transitions to `CANCELLED`
> (out of stock, payment failure) since those are part of the core
> placement flow, not a separate cancellation feature.

## High-Level Design

```
                    ┌────────────┐
   Customer ──HTTP──▶   Order     │
                    │  Service    │
                    └─────┬───────┘
                          │ publishes / consumes
                          ▼
                  ┌───────────────┐
                  │   Event Bus    │   (topic per event type,
                  │                │    partitioned by order_id)
                  └───────┬───────┘
              ┌───────────┼───────────────┐
              ▼           ▼               ▼
     ┌────────────┐ ┌────────────┐ ┌────────────────┐
     │ Inventory  │ │  Payment   │ │  Fulfillment    │
     │  Service   │ │  Service   │ │   Service       │
     └────────────┘ └────────────┘ └─────────────────┘
```

- **Order Service** — owns order records and the order state machine. Sole
  entry point for the customer (create, get — no cancel endpoint for now).
  Publishes `OrderCreated` and `OrderCancelled` (the latter only as a
  failure-compensation trigger, not customer-initiated); consumes
  `InventoryReserved`, `InventoryFailed`, `PaymentCompleted`,
  `PaymentFailed`, `InventoryReleased`, `PaymentRefunded` to advance/roll
  back order state.
- **Inventory Service** — owns stock levels. Consumes `OrderCreated`
  (reserve) and `OrderCancelled` (release, on payment failure). Publishes
  `InventoryReserved`, `InventoryFailed`, `InventoryReleased`.
- **Payment Service** — owns payment/charge records. Consumes
  `InventoryReserved` (charge). Publishes `PaymentCompleted`,
  `PaymentFailed`. (`PaymentRefunded` is defined for future use but has no
  producer path while cancellation is out of scope — a completed payment
  is never refunded in the current MVP flow.)
- **Fulfillment Service** — consumes `OrderConfirmed` to begin shipping.
  Publishes `OrderShipped`, `OrderDelivered` (simulated — no real carrier).

There is no orchestrator: each service reacts only to events on the bus and
publishes the next event(s). The Order Service is not a coordinator in the
saga sense — it's just another event consumer/producer that also happens to
own the order's state machine and expose the customer-facing API.

**Consistency approach:** each service owns exactly one piece of state
(orders, inventory, payments) and mutates it transactionally in response to
a single event, then publishes the outcome. Every consumer is idempotent:
before acting on an event it checks whether it has already processed that
event/order transition (e.g. by event id or by current entity state) and
no-ops if so. This makes at-least-once delivery safe without a saga
coordinator or distributed transaction.

## Order Placement Flow

1. Customer calls Order Service to place an order.
2. Order Service creates the order in state `CREATED`, persists it, and
   publishes `OrderCreated { orderId, items, ... }`.
3. Inventory Service consumes `OrderCreated`:
   - If all items have sufficient stock: reserve (decrement available
     stock) for every item, publish `InventoryReserved { orderId }`.
   - Else: publish `InventoryFailed { orderId, reason }` (no stock is
     decremented).
4. Order Service consumes the result:
   - `InventoryReserved` → order moves to `RESERVED`.
   - `InventoryFailed` → order moves to `CANCELLED` (reason: out of
     stock). Flow ends.
5. Payment Service consumes `InventoryReserved`:
   - Attempts to charge the customer's payment method, using the order id
     as an idempotency key so retried events never double-charge.
   - Success → publish `PaymentCompleted { orderId }`.
   - Failure → publish `PaymentFailed { orderId, reason }`.
6. Order Service consumes the result:
   - `PaymentCompleted` → order moves to `CONFIRMED`, publishes
     `OrderConfirmed { orderId }`.
   - `PaymentFailed` → order moves directly to `CANCELLED` and publishes
     `OrderCancelled { orderId, reason: payment_failed }` to trigger
     compensation (inventory release below).
7. Fulfillment Service consumes `OrderConfirmed` and begins shipping
   (simulated), eventually publishing `OrderShipped` then
   `OrderDelivered`; Order Service consumes these to update state.

**Compensation (failure) path:**
- On `PaymentFailed`, Order Service moves the order to `CANCELLED` and
  publishes `OrderCancelled`. Inventory Service consumes `OrderCancelled`
  for an order that had a reservation and releases the stock, publishing
  `InventoryReleased`. This release happens after the order is already
  `CANCELLED` — the order's terminal state doesn't wait on the
  compensation to complete, only the underlying inventory/payment records
  do.
- Note: `PaymentFailed` can only occur after inventory was successfully
  reserved (payment is only attempted once `InventoryReserved` fires), so
  the compensation on this path is always inventory release only — there
  is never a completed payment to refund at this point.

> **Order Cancellation Flow — out of scope for now.** Customer-initiated
> cancellation of a `RESERVED`/`CONFIRMED` order (and the resulting
> payment refund) is deferred; see the note under Functional Requirements.
> When it's added later, it will reuse `OrderCancelled` /
> `InventoryReleased` / `PaymentRefunded` the same way the failure path
> does.

## Order State Machine

**States** (as defined in the source article):

`CREATED → RESERVED → CONFIRMED → SHIPPED → DELIVERED`

with `CANCELLED` as the failure/cancellation path from `CREATED`,
`RESERVED`, or `CONFIRMED`, and `REFUND_PENDING → REFUNDED` as the
post-delivery return path from `DELIVERED`.

**Transitions:**

| From | Event consumed | To |
|---|---|---|
| — | customer places order | `CREATED` |
| `CREATED` | `InventoryReserved` | `RESERVED` |
| `CREATED` | `InventoryFailed` | `CANCELLED` |
| `RESERVED` | `PaymentCompleted` | `CONFIRMED` |
| `RESERVED` | `PaymentFailed` | `CANCELLED` |
| `CONFIRMED` | fulfillment ships order | `SHIPPED` |
| `SHIPPED` | fulfillment delivers order | `DELIVERED` |
| `DELIVERED` | customer requests return (post-MVP) | `REFUND_PENDING` |
| `REFUND_PENDING` | refund processed (post-MVP) | `REFUNDED` |

Notes:
- `CANCELLED` is only reachable from `CREATED` (inventory unavailable) or
  `RESERVED` (payment failed) in the current MVP — both are failure paths
  within the placement flow, not customer-initiated cancellation.
  Customer-initiated cancellation (from `RESERVED` or `CONFIRMED`) is out
  of scope for now; see Functional Requirements.
- `SHIPPED` and `DELIVERED` are terminal for cancellation purposes.
- The `DELIVERED → REFUND_PENDING → REFUNDED` path exists in the state
  machine per the source article but the return/refund flow itself is out
  of scope for this MVP (no functional requirement covers it yet).
- Every transition is caused by exactly one consumed event (or the direct
  customer action that creates the order) and is persisted as an
  append-only order-events record for audit purposes.

```
                 ┌─────────┐
                 │ CREATED │
                 └────┬────┘
        InventoryFailed   InventoryReserved
                 │        └──────────────┐
                 ▼                       ▼
           ┌───────────┐        ┌───────────────┐
           │ CANCELLED │◀───────│   RESERVED    │
           └───────────┘  PaymentFailed └───┬───┘
                                   PaymentCompleted
                                             ▼
                                      ┌────────────┐
                                      │ CONFIRMED  │
                                      └─────┬──────┘
                                        ships │
                                             ▼
                                      ┌────────────┐
                                      │  SHIPPED   │
                                      └─────┬──────┘
                                     delivers │
                                             ▼
                                      ┌────────────┐
                                      │ DELIVERED  │
                                      └─────┬──────┘
                                  return (post-MVP) │
                                             ▼
                                    ┌────────────────┐
                                    │ REFUND_PENDING │
                                    └───────┬────────┘
                                            ▼
                                     ┌────────────┐
                                     │  REFUNDED  │
                                     └────────────┘
```

(No arrow from `RESERVED`/`CONFIRMED` back to `CANCELLED` via customer
cancellation — that transition is out of scope for now.)

## Events

| Event | Producer | Consumer(s) | Payload (minimum) |
|---|---|---|---|
| `OrderCreated` | Order Service | Inventory Service | `orderId, items[]` |
| `InventoryReserved` | Inventory Service | Order Service, Payment Service | `orderId` |
| `InventoryFailed` | Inventory Service | Order Service | `orderId, reason` |
| `PaymentCompleted` | Payment Service | Order Service | `orderId, paymentId` |
| `PaymentFailed` | Payment Service | Order Service | `orderId, reason` |
| `OrderConfirmed` | Order Service | Fulfillment Service | `orderId` |
| `OrderCancelled` | Order Service | Inventory Service | `orderId, reason` |
| `InventoryReleased` | Inventory Service | Order Service | `orderId` |
| `PaymentRefunded` | Payment Service | Order Service | `orderId, paymentId` |
| `OrderShipped` | Fulfillment Service | Order Service | `orderId` |
| `OrderDelivered` | Fulfillment Service | Order Service | `orderId` |

`OrderCreated`, `InventoryReserved`, `InventoryFailed`, `PaymentCompleted`,
`PaymentFailed`, and `OrderConfirmed` are the events named in the source
article. `OrderCancelled`, `InventoryReleased`, `OrderShipped`, and
`OrderDelivered` are added here because the article references
compensation and fulfillment ("release inventory, refund payment",
"Fulfillment Service picks up confirmed order for shipping") without
naming their events explicitly. `PaymentRefunded` is defined for
completeness but is currently unused — no flow in this MVP produces it,
since customer-initiated cancellation (the only source of refunds on a
completed payment) is out of scope for now.

**`reason` values** (the three events above that carry a `reason` field use
a fixed vocabulary, not free text):
- `InventoryFailed.reason`: `OutOfStock` — the only cause this spec
  defines (insufficient stock on one or more items).
- `OrderCancelled.reason`: `PaymentFailed` — the only cause this spec
  defines; per Order Placement Flow step 6, `OrderCancelled` is only
  published in this MVP as a result of a failed payment.
- `PaymentFailed.reason`: not decomposed into a fixed set of sub-reasons
  by this spec (e.g. distinguishing a card decline from a gateway error)
  — treated as an opaque diagnostic string, since no functional
  requirement branches on it.

Events are partitioned/routed by `orderId` so that all events for a given
order are processed in order by each consumer.

## Tech Stack

> This section (and Data Model below) are the only places a concrete
> technology is named. Everything above (Objective, NFRs, Functional
> Requirements, High-Level Design, flows, state machine, Events) is
> technology-agnostic by design and must stay that way — read it without
> these sections and it should still make complete sense. Nothing here
> changes the architecture described above; it only pins down what it
> runs on.

**Language / runtime:** C# on .NET 10 (LTS). Each of the four services
(Order, Inventory, Payment, Fulfillment) is a separate .NET service/process.

**Cloud platform:** Azure.
- Compute: Azure Container Apps running each service as an independent
  container/app. Chosen over AKS (unnecessary orchestration overhead for
  4 services) and over Azure Functions (Order Service is both an HTTP API
  and a Service Bus consumer in one logical service — Functions would
  force splitting that across trigger types/apps for no benefit).
- Event bus: Azure Service Bus topics/subscriptions — one topic per event
  type in the Events table below, with `orderId` used as the Service Bus
  session id so per-order events stay ordered per consumer, matching the
  "partitioned by orderId" requirement above. Chosen over Event Grid (no
  strong ordering guarantees) and Event Hubs (built for high-throughput
  streaming, not this event flow's transactional needs).
- Per-service state: Azure SQL Database, **Serverless tier**, one
  database per service, not shared — matching "each service owns exactly
  one piece of state." Serverless tier auto-pauses when idle, keeping
  demo cost near zero while still giving Inventory/Payment the
  relational/transactional guarantees the consistency NFRs require.
  Cosmos DB was considered and ruled out on cost (RU-based pricing) with
  no offsetting benefit for this access pattern.
- Idempotency/audit: order-events append log stored alongside the Order
  Service's own database.

**CI/CD:** GitHub Actions.

**Infrastructure as code:** Terraform, provisioning:
- Resource group(s) for the demo
- Azure Container Apps environment + one app/deployment per service
- Azure Service Bus namespace + topics/subscriptions per event type
- Azure SQL Database (Serverless tier) instance per service
- Any required networking, identity (managed identities for service-to-
  service and service-to-bus auth), and secrets (Azure Key Vault)

**Explicitly not decided here (deferred to Plan phase):**
- Project layout (solution structure, one repo vs. multiple)

## Data Model

Adapted from the source article's five tables, with column names switched
to C# naming convention (PascalCase, e.g. `OrderId` instead of
`order_id`) and .NET types substituted for their SQL equivalents. Grouped
by which service owns the table, per the "each service owns exactly one
piece of state" rule in High-Level Design — each group lives in that
service's own Azure SQL database, not a shared one.

**Order Service**

`Orders`

| Column | Type | Purpose |
|---|---|---|
| OrderId | Guid | Primary key |
| UserId | Guid | Customer reference |
| Status | OrderStatus | `Created / Reserved / Confirmed / Shipped / Delivered / Cancelled` (`RefundPending` / `Refunded` also exist per the source article's state machine but are unused — refunds are out of scope for this MVP) |
| TotalAmount | decimal | Order value |
| ShippingAddress | string (JSON) | Delivery location — shape not specified by the source article |
| PaymentMethod | string | Payment identifier |
| CreatedAt | DateTimeOffset | Creation timestamp |
| UpdatedAt | DateTimeOffset | Last modification timestamp |

`OrderItems`

| Column | Type | Purpose |
|---|---|---|
| OrderId | Guid | Foreign key to `Orders` |
| ProductId | string | SKU identifier |
| Quantity | int | Unit count |
| UnitPrice | decimal | Price per unit at purchase |
| Subtotal | decimal | `Quantity * UnitPrice` |

`OrderEvents` (append-only audit log — Non-Functional Requirements:
"Auditable state")

| Column | Type | Purpose |
|---|---|---|
| EventId | Guid | Primary key |
| OrderId | Guid | Foreign key to `Orders` |
| EventType | string | Event classification, e.g. `InventoryReserved` |
| FromState | OrderStatus? | Previous order state |
| ToState | OrderStatus | New order state |
| EventData | string (JSON) | Metadata and context |
| CreatedAt | DateTimeOffset | Event occurrence time |

**Inventory Service**

`InventoryItems`

| Column | Type | Purpose |
|---|---|---|
| ProductId | string | Primary key (SKU) |
| Available | int | Purchasable quantity |
| Reserved | int | Quantity held by pending orders |
| Version | int | Optimistic-lock counter per the source article — locking is out of scope for this MVP, so this column is unused |
| UpdatedAt | DateTimeOffset | Last modification time |

**Payment Service**

`Payments`

| Column | Type | Purpose |
|---|---|---|
| PaymentId | Guid | Primary key |
| OrderId | Guid | Foreign key to `Orders` |
| IdempotencyKey | string | Duplicate-charge prevention identifier |
| Amount | decimal | Charged value |
| Status | PaymentStatus | `Pending / Completed / Failed` (`Refunded` also exists per the source article but is unused — refunds are out of scope for this MVP) |
| ProviderId | string | External payment gateway reference |
| CreatedAt | DateTimeOffset | Initiation time |
| CompletedAt | DateTimeOffset? | Completion time |

## Boundaries

- **Always do:** keep the spec technology-agnostic at this stage; keep
  Cart, Notification, Analytics, and Search out of scope; keep every event
  consumer idempotent; record every state transition; use only the states
  defined in the source article (no invented states).
- **Ask first:** introducing an orchestrator/saga coordinator, adding
  locking or TTL-based reservation semantics, adding services beyond the
  four listed, changing the tech stack from what's pinned in the Tech
  Stack section, adding customer-initiated order cancellation, adding
  refund handling.
- **Never do:** mark an order `CONFIRMED` without a corresponding
  successful payment event; decrement inventory outside of the
  `OrderCreated`-triggered reservation step.

## Success Criteria

- All functional requirements above are satisfiable by the design.
- Placing an order with sufficient stock and a successful charge reaches
  `CONFIRMED`.
- Placing an order with insufficient stock reaches `CANCELLED` without any
  payment attempt.
- Placing an order with sufficient stock but a failed charge reaches
  `CANCELLED` with inventory released.
- Re-delivering any event to its consumer does not change the end state
  (idempotency holds).

## Open Questions

- When customer-initiated order cancellation is brought into scope: should
  partial-item cancellation/refunds be supported, or is cancellation
  always whole-order?
- What identifies a "payment method" at the API boundary — out of scope
  for a tech-agnostic spec, to be decided at implementation time.
