Design: Distributed Order Processing Demo

Purpose: A minimal but real distributed system on Azure to practice async messaging patterns, using Terraform for infra and C# for services.

Architecture

Client ──HTTP──> Order API (Azure Function, HTTP trigger)
                     │
                     ├─ writes order record ──> Azure Table Storage
                     │
                     └─ publishes "OrderCreated" event ──> Service Bus Topic
                                                              │
                                        ┌─────────────────────┴─────────────────────┐
                                        │                                           │
                              Subscription: inventory                    Subscription: notification
                                        │                                           │
                             Inventory Worker (Function,                Notification Worker (Function,
                             Service Bus trigger)                       Service Bus trigger)
                                        │                                           │
                             updates order status in                    logs/simulates a
                             Table Storage                               notification (e.g. writes
                                                                          to console/App Insights)

Components

1. Order API — HTTP-triggered Azure Function (C#). Accepts POST /orders, validates, writes an order row to Table Storage with status Created, publishes an OrderCreated message to the Service Bus topic. Also exposes GET /orders/{id} to check status.
2. Service Bus Topic (order-events) with two subscriptions: inventory-sub and notification-sub — each downstream worker gets its own copy of every message (pub/sub fan-out).
3. Inventory Worker — Service Bus-triggered Function. Consumes from inventory-sub, simulates a stock check/reservation, updates the order's status in Table Storage to InventoryReserved.
4. Notification Worker — Service Bus-triggered Function. Consumes from notification-sub, simulates sending a notification (just logs it, no real email/SMS needed for a demo).
5. Table Storage — one table holding order records (PartitionKey/RowKey, status, timestamps).

Data flow

Order created → row written (Created) → event published → both workers receive their own copy concurrently → Inventory worker updates status → Notification worker logs a message. Client can poll GET /orders/{id} to watch status change.

Error handling / resilience

Kept intentionally light since messaging is the focus, not full resilience: Service Bus's built-in retry + dead-letter queue (default function bindings retry automatically) is enough to observe the pattern — no custom circuit breakers or saga compensation needed for this scope.

Infra (Terraform)

Single Terraform config (local state) provisioning: Resource Group, Storage Account (Function storage + Table Storage), Service Bus namespace + topic + 2 subscriptions, 3 Function Apps (Consumption plan) with app settings wired to the connection strings via Terraform outputs.

Testing

Manual/exploratory for this demo — curl/HTTP file to POST an order, then poll GET /orders/{id} and watch status transition, plus check Azure Portal / func local logs to see both workers fire independently.