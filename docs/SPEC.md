Design: Distributed Order Processing Demo

Purpose: A minimal but real distributed system on Azure to practice async messaging patterns, using Terraform for infra and C# for services. Scoped as a personal learning demo, not a production system — no auth, no pricing/catalog logic, manual/exploratory testing only.

Architecture

Client ──HTTP──> Order API (Azure Function, HTTP trigger)
                     │
                     ├─ writes order record ──> Azure Table Storage (Orders)
                     │
                     └─ publishes "OrderCreated" ──> Service Bus Topic (order-events)
                                                              │
                                                    Subscription: inventory-sub
                                                              │
                                                   Inventory Worker (Function,
                                                   Service Bus trigger)
                                                              │
                                        ┌─────────────────────┴─────────────────────┐
                                        │                                           │
                             checks/decrements stock in                  updates order status in
                             Table Storage (Inventory)                   Table Storage (Orders):
                                        │                                 InventoryReserved or
                                        │                                 InventoryFailed
                                        │
                             publishes "InventoryReserved" or
                             "InventoryFailed" ──> Service Bus Topic
                             (order-status-events)
                                        │
                                    Subscription: notification-sub
                                        │
                             Notification Worker (Function,
                             Service Bus trigger)
                                        │
                             logs/simulates a notification whose
                             content differs by outcome (confirmed
                             vs. failed/out-of-stock)

Components

1. Order API — HTTP-triggered Azure Function (C#), its own Function App. Accepts `POST /orders`, validates, writes an order row to Table Storage (status `Created`), publishes an `OrderCreated` message to the `order-events` topic. Also exposes `GET /orders/{id}` to check status.
   - Request body: `{ customerId: guid, items: [{ skuId: guid, name: string, quantity: int }] }`.
   - `orderId` is generated server-side (guid).
   - No auth on endpoints — out of scope for this demo.

2. Inventory Worker — Service Bus-triggered Function, its own Function App. Consumes `OrderCreated` from `inventory-sub`. All-or-nothing stock check against the `Inventory` table: if every SKU in the order has sufficient `quantityOnHand`, decrements each (optimistic concurrency via ETag) and sets the order's status to `InventoryReserved`; if any SKU is short, sets status to `InventoryFailed` (no decrement). No in-process retry on ETag conflicts or any other failure — any exception is left to Service Bus's built-in redelivery/DLQ, per the resilience philosophy below. Publishes an `InventoryReserved` or `InventoryFailed` event (with `orderId`, `customerId`, `status`, and — on failure — which SKUs were short) to the `order-status-events` topic.

3. Notification Worker — Service Bus-triggered Function, its own Function App. Consumes from `notification-sub` on `order-status-events`. Simulates a notification (log only, no real email/SMS) whose message content differs based on the outcome — a "confirmed" message for `InventoryReserved`, a "failed/out-of-stock" message for `InventoryFailed`.

4. Service Bus — two topics, each with a single subscription:
   - `order-events` topic → `inventory-sub` (carries `OrderCreated`)
   - `order-status-events` topic → `notification-sub` (carries `InventoryReserved` / `InventoryFailed`)
   Two topics rather than one topic with filtered subscriptions: each subscription's contract stays a single message shape, and the two distinct domain events (order placed vs. inventory outcome) map directly onto two topics — easier to reason about and observe in the portal.

5. Table Storage — two tables:
   - `Orders` — PartitionKey `customerId`, RowKey `orderId` (guid). Fields: `items` (JSON), `status`, `createdAt`, `updatedAt`.
   - `Inventory` — fixed PartitionKey, RowKey `skuId` (guid). Fields: `name`, `quantityOnHand`. Not seeded by Terraform — seeded manually after `apply` via a script or `.http` request, kept separate from infra provisioning so stock levels can be iterated on without a `terraform apply`.

Data flow

Order created → row written (`Created`) → `OrderCreated` published → Inventory Worker checks stock → decrements + `InventoryReserved`, or `InventoryFailed` if short → Inventory Worker publishes the outcome event → Notification Worker logs a message matching the outcome. Client can poll `GET /orders/{id}` to watch status transition from `Created` to `InventoryReserved` or `InventoryFailed`.

Error handling / resilience

Kept intentionally light since messaging is the focus, not full resilience: Service Bus's built-in retry + dead-letter queue (default function bindings retry automatically) is enough to observe the pattern. No custom circuit breakers, saga compensation, or in-process retry loops — including for ETag conflicts on the Inventory table, which are left to Service Bus redelivery rather than handled with a retry loop in the worker.

Infra (Terraform)

Single Terraform config (local state) provisioning: Resource Group, Storage Account (Function storage + both Table Storage tables), Service Bus namespace + 2 topics (`order-events`, `order-status-events`) + 2 subscriptions (`inventory-sub`, `notification-sub`), 3 separate Function Apps (Consumption plan, one per service, each its own Terraform resource) with app settings wired to the connection strings via Terraform outputs. Inventory seed data is explicitly not managed by Terraform.

Repo/project structure

Three separate Function App projects (own `.csproj` per service — Order API, Inventory Worker, Notification Worker), matching the three separate Terraform Function App resources. This is deliberate: the point of the demo is practicing independently deployable services, not a single multi-function app.

Testing

Manual/exploratory for this demo — curl/HTTP file to POST an order, then poll `GET /orders/{id}` and watch status transition (`Created` → `InventoryReserved` or `InventoryFailed`), plus check Azure Portal / func local logs to see the Inventory Worker fire, publish its outcome event, and the Notification Worker react with the matching message. A seed script/`.http` file populates the `Inventory` table before testing.
