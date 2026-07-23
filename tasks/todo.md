# Todo: Order System MVP

Each phase = its own branch off `main` + its own PR. See `tasks/plan.md`
"Delivery / branching strategy" for details.

## Phase 0 — Shared foundation
- [ ] 1. Solution scaffolding (`OrderSystem.sln`, project skeletons, build props)
- [ ] 2. Event contracts (DTOs for all 11 events, OrderStatus/PaymentStatus enums)
- [ ] 3. Messaging abstraction (IEventPublisher/IEventSubscriber, Service Bus + in-memory impls)
- [ ] 4. Terraform remote state bootstrap (storage account + container, local state, one-time manual apply)
- [ ] 5. Terraform shared foundation (resource group, Container Apps environment, Service Bus namespace, Key Vault, identities)
- [ ] 6. CI/CD skeleton (reusable build/test workflow + terraform plan/apply job for shared/)

## Phase 1 — Order Service
- [ ] 7. Order domain + persistence (Orders/OrderItems/OrderEvents, state machine)
- [ ] 8. Order Service HTTP API (POST /orders, GET /orders/{id})
- [ ] 9. Order Service event consumers (Inventory*/Payment*/OrderShipped/OrderDelivered → state transitions)
- [ ] 10. Order Service Terraform + deploy (Container App, SQL DB, owned topics: OrderCreated/OrderCancelled/OrderConfirmed; CI/CD deploy job)

**Checkpoint: Order Service deployed and testable standalone.**

## Phase 2 — Inventory Service
- [ ] 11. Inventory domain + persistence (InventoryItems)
- [ ] 12. Reserve on OrderCreated (all-or-nothing, publishes InventoryReserved/InventoryFailed)
- [ ] 13. Release on OrderCancelled (publishes InventoryReleased)
- [ ] 14. Inventory Service Terraform + deploy (Container App, SQL DB, subscriptions to OrderCreated/OrderCancelled, owned topics: InventoryReserved/InventoryFailed/InventoryReleased + Order's new subscriptions to them; CI/CD deploy job)

## Phase 3 — Payment Service
- [ ] 15. Payment domain + persistence (Payments)
- [ ] 16. Charge on InventoryReserved (idempotent, publishes PaymentCompleted/PaymentFailed)
- [ ] 17. Payment Service Terraform + deploy (Container App, SQL DB, subscription to InventoryReserved, owned topics: PaymentCompleted/PaymentFailed + Order's new subscriptions to them; CI/CD deploy job)

## Phase 4 — Fulfillment Service
- [ ] 18. Simulated shipping on OrderConfirmed (publishes OrderShipped, OrderDelivered)
- [ ] 19. Fulfillment Service Terraform + deploy (Container App, no DB, subscription to OrderConfirmed, owned topics: OrderShipped/OrderDelivered + Order's new subscriptions to them; CI/CD deploy job)

**Checkpoint: all four services deployed and wired end-to-end on real Azure infra.**

## Phase 5 — End-to-end verification & docs
- [ ] 20. Integration test suite (happy path, out-of-stock, payment-failed, idempotency re-delivery)
- [ ] 21. README (local dev instructions, link to docs/SPEC.md, one-time bootstrap command from Phase 0 task 4)

## Explicitly not built
`PaymentRefunded` — no topic, subscription, producer, or consumer anywhere
in this plan (refunds/cancellation out of scope per spec).
