# Spec: E-Commerce Order System (MVP)

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

**Success looks like:** a customer can place an order, the system reserves
inventory and processes payment via independent services reacting to events
(no central orchestrator), the order reaches a terminal state consistent
with what actually happened to inventory and payment, and the order can be
cancelled pre-shipment with inventory released and payment refunded.

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
2. **Reserve inventory** — system checks and reserves stock for every item
   in the order as an all-or-nothing operation.
3. **Process payment** — system charges the customer for the order once
   inventory is confirmed reserved.
4. **Confirm order** — once inventory is reserved and payment succeeds,
   the order is confirmed and handed to fulfillment.
5. **Track order status** — customer can query current order status at any
   time.
6. **Cancel order** — customer can cancel an order before it ships;
   reserved inventory is released and any completed payment is refunded.
7. **Handle failures gracefully** — if inventory can't be reserved, or
   payment fails, the order is automatically moved to a failed/cancelled
   state and any partial work (e.g. a reservation made before a later
   failure) is undone via a compensating event.

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
                  │ (choreography) │    partitioned by order_id)
                  └───────┬───────┘
              ┌───────────┼───────────────┐
              ▼           ▼               ▼
     ┌────────────┐ ┌────────────┐ ┌────────────────┐
     │ Inventory  │ │  Payment   │ │  Fulfillment    │
     │  Service   │ │  Service   │ │   Service       │
     └────────────┘ └────────────┘ └─────────────────┘
```

- **Order Service** — owns order records and the order state machine. Sole
  entry point for the customer (create, get, cancel). Publishes
  `OrderPlaced` and `OrderCancelled`; consumes `InventoryReserved`,
  `InventoryReservationFailed`, `PaymentCompleted`, `PaymentFailed`,
  `InventoryReleased`, `PaymentRefunded` to advance/roll back order state.
- **Inventory Service** — owns stock levels. Consumes `OrderPlaced` (reserve)
  and `OrderCancelled` (release). Publishes `InventoryReserved`,
  `InventoryReservationFailed`, `InventoryReleased`.
- **Payment Service** — owns payment/charge records. Consumes
  `InventoryReserved` (charge) and `OrderCancelled` (refund, if a charge
  exists). Publishes `PaymentCompleted`, `PaymentFailed`, `PaymentRefunded`.
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
2. Order Service creates the order in state `PLACED`, persists it, and
   publishes `OrderPlaced { orderId, items, ... }`.
3. Inventory Service consumes `OrderPlaced`:
   - If all items have sufficient stock: reserve (decrement available
     stock) for every item, publish `InventoryReserved { orderId }`.
   - Else: publish `InventoryReservationFailed { orderId, reason }`
     (no stock is decremented).
4. Order Service consumes the result:
   - `InventoryReserved` → order moves to `INVENTORY_RESERVED`.
   - `InventoryReservationFailed` → order moves to `CANCELLED`
     (reason: out of stock). Flow ends.
5. Payment Service consumes `InventoryReserved`:
   - Attempts to charge the customer's payment method, using the order id
     as an idempotency key so retried events never double-charge.
   - Success → publish `PaymentCompleted { orderId }`.
   - Failure → publish `PaymentFailed { orderId, reason }`.
6. Order Service consumes the result:
   - `PaymentCompleted` → order moves to `CONFIRMED`, publishes
     `OrderConfirmed { orderId }`.
   - `PaymentFailed` → order moves to `CANCELLING`, publishes
     `OrderCancelled { orderId, reason: payment_failed }` to trigger
     compensation (inventory release below).
7. Fulfillment Service consumes `OrderConfirmed` and begins shipping
   (simulated), eventually publishing `OrderShipped` then
   `OrderDelivered`; Order Service consumes these to update state.

**Compensation (failure) path:**
- On `PaymentFailed`, Order Service publishes `OrderCancelled`. Inventory
  Service consumes `OrderCancelled` for an order that had a reservation and
  releases the stock, publishing `InventoryReleased`. Order Service
  consumes `InventoryReleased` and moves the order to its terminal
  `CANCELLED` state.
- On a customer-initiated cancellation (see below), the same
  `OrderCancelled` event drives both inventory release and payment refund
  in parallel — each service only acts if it actually holds relevant state
  for that order (Inventory Service no-ops if no reservation exists;
  Payment Service no-ops/refunds only if a completed charge exists).

## Order Cancellation Flow

1. Customer calls Order Service to cancel an order that is not yet
   `SHIPPED`/`DELIVERED`.
2. Order Service validates the order is in a cancellable state, moves it to
   `CANCELLING`, and publishes `OrderCancelled { orderId }`.
3. Inventory Service consumes `OrderCancelled`: if a reservation exists for
   this order, release it and publish `InventoryReleased`; otherwise no-op.
4. Payment Service consumes `OrderCancelled`: if a completed charge exists
   for this order, refund it and publish `PaymentRefunded`; otherwise
   no-op.
5. Order Service waits for the relevant compensations (inventory
   release if a reservation had been made, refund if a charge had been
   made) and then moves the order to the terminal `CANCELLED` state.

## Order State Machine

**States:**

`PLACED → INVENTORY_RESERVED → CONFIRMED → SHIPPED → DELIVERED`

with `CANCELLING` and `CANCELLED` as the failure/cancellation path, and
`REFUND_PENDING → REFUNDED` when an already-charged order is cancelled.

**Transitions:**

| From | Event consumed | To |
|---|---|---|
| — | customer places order | `PLACED` |
| `PLACED` | `InventoryReserved` | `INVENTORY_RESERVED` |
| `PLACED` | `InventoryReservationFailed` | `CANCELLED` |
| `INVENTORY_RESERVED` | `PaymentCompleted` | `CONFIRMED` |
| `INVENTORY_RESERVED` | `PaymentFailed` | `CANCELLING` |
| `INVENTORY_RESERVED` / `CONFIRMED` | customer cancels | `CANCELLING` |
| `CANCELLING` | `InventoryReleased` and (no charge existed or `PaymentRefunded` received) | `CANCELLED` |
| `CANCELLING` | `PaymentRefunded` (charge existed, no reservation to release) | `REFUND_PENDING` → `CANCELLED` once release confirmed, or directly `CANCELLED` if nothing to release |
| `CONFIRMED` | fulfillment ships order | `SHIPPED` |
| `SHIPPED` | fulfillment delivers order | `DELIVERED` |

Notes:
- `CANCELLING` is a transient state: the order sits there until every
  compensation it's waiting on (inventory release, refund) has been
  confirmed, then it moves to `CANCELLED`. If it was cancelled before any
  reservation/charge existed, it moves to `CANCELLED` immediately.
- `SHIPPED` and `DELIVERED` are terminal for cancellation purposes — once
  `SHIPPED`, cancellation is no longer allowed in this MVP (returns/refunds
  post-shipment are out of scope).
- Every transition is caused by exactly one consumed event (or the direct
  customer action that creates/cancels the order) and is persisted as an
  append-only order-events record for audit purposes.

```
                 ┌────────┐
                 │ PLACED │
                 └───┬────┘
      InventoryReservationFailed   InventoryReserved
                 │        └──────────────┐
                 ▼                       ▼
           ┌───────────┐        ┌─────────────────────┐
           │ CANCELLED │◀───┐   │ INVENTORY_RESERVED   │
           └───────────┘    │   └──────────┬───────────┘
                 ▲           │      PaymentCompleted │  PaymentFailed
                 │           │              ▼         └───────┐
                 │           │       ┌────────────┐           ▼
                 │           │       │ CONFIRMED  │      ┌────────────┐
                 │           │       └─────┬──────┘      │ CANCELLING │
                 │           │      ships   │             └─────┬──────┘
                 │           │             ▼           compensations done
                 │           │       ┌────────────┐            │
                 │           │       │  SHIPPED   │             │
                 │           │       └─────┬──────┘             │
                 │           │    delivers  │                    │
                 │           │             ▼                    │
                 │           │       ┌────────────┐              │
                 │           │       │ DELIVERED  │              │
                 │           │       └────────────┘              │
                 │           └────────────────────────────────────┘
                 └───────────────── (cancel from INVENTORY_RESERVED/CONFIRMED) 
```

## Events

| Event | Producer | Consumer(s) | Payload (minimum) |
|---|---|---|---|
| `OrderPlaced` | Order Service | Inventory Service | `orderId, items[]` |
| `InventoryReserved` | Inventory Service | Order Service, Payment Service | `orderId` |
| `InventoryReservationFailed` | Inventory Service | Order Service | `orderId, reason` |
| `PaymentCompleted` | Payment Service | Order Service | `orderId, paymentId` |
| `PaymentFailed` | Payment Service | Order Service | `orderId, reason` |
| `OrderConfirmed` | Order Service | Fulfillment Service | `orderId` |
| `OrderCancelled` | Order Service | Inventory Service, Payment Service | `orderId, reason` |
| `InventoryReleased` | Inventory Service | Order Service | `orderId` |
| `PaymentRefunded` | Payment Service | Order Service | `orderId, paymentId` |
| `OrderShipped` | Fulfillment Service | Order Service | `orderId` |
| `OrderDelivered` | Fulfillment Service | Order Service | `orderId` |

Events are partitioned/routed by `orderId` so that all events for a given
order are processed in order by each consumer.

## Boundaries

- **Always do:** keep the spec technology-agnostic at this stage; keep
  Cart, Notification, Analytics, and Search out of scope; keep every event
  consumer idempotent; record every state transition.
- **Ask first:** introducing an orchestrator/saga coordinator, adding
  locking or TTL-based reservation semantics, adding services beyond the
  four listed, choosing a concrete tech stack/message bus.
- **Never do:** mark an order `CONFIRMED` without a corresponding
  successful payment event; decrement inventory outside of the
  `OrderPlaced`-triggered reservation step.

## Success Criteria

- All six functional requirements above are satisfiable by the design.
- Placing an order with sufficient stock and a successful charge reaches
  `CONFIRMED`.
- Placing an order with insufficient stock reaches `CANCELLED` without any
  payment attempt.
- Placing an order with sufficient stock but a failed charge reaches
  `CANCELLED` with inventory released.
- Cancelling a `CONFIRMED` (unshipped) order reaches `CANCELLED` with
  inventory released and payment refunded.
- Re-delivering any event to its consumer does not change the end state
  (idempotency holds).

## Open Questions

- Should partial-item cancellation/refunds be supported later, or is
  cancellation always whole-order? (Assumed whole-order for MVP.)
- What identifies a "payment method" at the API boundary — out of scope
  for a tech-agnostic spec, to be decided at implementation time.
