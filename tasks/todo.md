# Todo: Order System MVP

## Phase 0 — Shared foundation
- [ ] 1. Solution scaffolding (`OrderSystem.sln`, project skeletons, build props)
- [ ] 2. Event contracts (DTOs for all 11 events, OrderStatus/PaymentStatus enums)
- [ ] 3. Messaging abstraction (IEventPublisher/IEventSubscriber, Service Bus + in-memory impls)

## Phase 1 — Order Service
- [ ] 4. Order domain + persistence (Orders/OrderItems/OrderEvents, state machine)
- [ ] 5. Order Service HTTP API (POST /orders, GET /orders/{id})
- [ ] 6. Order Service event consumers (Inventory*/Payment*/OrderShipped/OrderDelivered → state transitions)

**Checkpoint: Order Service testable standalone via in-memory bus.**

## Phase 2 — Inventory Service
- [ ] 7. Inventory domain + persistence (InventoryItems)
- [ ] 8. Reserve on OrderCreated (all-or-nothing, publishes InventoryReserved/InventoryFailed)
- [ ] 9. Release on OrderCancelled (publishes InventoryReleased)

## Phase 3 — Payment Service
- [ ] 10. Payment domain + persistence (Payments)
- [ ] 11. Charge on InventoryReserved (idempotent, publishes PaymentCompleted/PaymentFailed)

## Phase 4 — Fulfillment Service
- [ ] 12. Simulated shipping on OrderConfirmed (publishes OrderShipped, OrderDelivered)

**Checkpoint: all four services exist.**

## Phase 5 — End-to-end verification
- [ ] 13. Integration test suite (happy path, out-of-stock, payment-failed, idempotency re-delivery)

## Phase 6 — Infrastructure as code
- [ ] 14a. Terraform remote state bootstrap (storage account + container, local state, one-time manual apply)
- [ ] 14b. Terraform main config (azurerm backend, resource group, Container Apps, Service Bus, Azure SQL x3, identities, Key Vault)

## Phase 7 — CI/CD
- [ ] 15. GitHub Actions (build/test on PR, image build, deploy workflow against 14a's remote backend)

## Phase 8 — Docs
- [ ] 16. README (local dev instructions, link to docs/SPEC.md, one-time bootstrap command from 14a)
