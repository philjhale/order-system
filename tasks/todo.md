# Todo: Order System MVP

Each phase = its own branch off `main` + its own PR. See `tasks/plan.md`
"Delivery / branching strategy" for details.

## Phase 0 — Shared foundation
- [ ] 1. Solution scaffolding (`shared/` Contracts+Messaging skeletons; per-service `services/<name>/` folders each with own `.sln`, `src/`, `tests/`, referencing `shared/` by relative path; `integration-tests/` skeleton; root build props)
- [ ] 2. Event contracts (DTOs for all 11 events, OrderStatus/PaymentStatus enums)
- [ ] 3. Messaging abstraction (IEventPublisher/IEventSubscriber w/ explicit abandon-for-redelivery outcome, Service Bus + in-memory impls)
- [ ] 4. Terraform remote state bootstrap (storage account + container, local state, one-time manual apply)
- [ ] 5. Terraform shared foundation (resource group, Container Apps environment, Service Bus namespace, Key Vault, identities, shared Azure Container Registry)
- [ ] 6. CI/CD skeleton (reusable build/test/docker-build-push workflow + terraform plan/apply job for shared/; when a PR touches two service Terraform folders, subscribing service's apply job declares `needs:` on topic-owning service's apply job)

## Phase 1 — Order Service
- [ ] 7. Order domain + persistence (Orders/OrderItems/OrderEvents, state machine)
- [ ] 8. Order Service HTTP API (POST /orders, GET /orders/{id})
- [ ] 9. Order Service event consumers (InventoryReserved/InventoryFailed/PaymentCompleted/PaymentFailed/OrderShipped/OrderDelivered → state transitions; InventoryReleased consumed + audit-logged, no transition; events arriving before their precondition state is reached are abandoned for redelivery, not rejected/dropped — relies on subscription max-delivery-count for poison messages)
- [ ] 10. Order Service Dockerfile, Terraform + deploy (`services/order-service/Dockerfile` + `services/order-service/infra/terraform/`: Container App image from shared ACR, SQL DB, owned topics: OrderCreated/OrderCancelled/OrderConfirmed; path-filtered CI/CD build/push/deploy job)

**Checkpoint: Order Service deployed and testable standalone.**

## Phase 2 — Inventory Service
- [ ] 11. Inventory domain + persistence (InventoryItems)
- [ ] 12. Reserve on OrderCreated (all-or-nothing, publishes InventoryReserved/InventoryFailed)
- [ ] 13. Release on OrderCancelled (publishes InventoryReleased)
- [ ] 14. Inventory Service Dockerfile, Terraform + deploy (`services/inventory-service/Dockerfile` + `services/inventory-service/infra/terraform/`: Container App image from shared ACR, SQL DB, subscriptions to OrderCreated/OrderCancelled, owned topics: InventoryReserved/InventoryFailed/InventoryReleased + Order's new subscriptions to them; path-filtered CI/CD build/push/deploy job; Order's apply job `needs:` Inventory's apply job)

## Phase 3 — Payment Service
- [ ] 15. Payment domain + persistence (Payments)
- [ ] 16. Charge on InventoryReserved (idempotent, publishes PaymentCompleted/PaymentFailed)
- [ ] 17. Payment Service Dockerfile, Terraform + deploy (`services/payment-service/Dockerfile` + `services/payment-service/infra/terraform/`: Container App image from shared ACR, SQL DB, subscription to InventoryReserved, owned topics: PaymentCompleted/PaymentFailed + Order's new subscriptions to them; path-filtered CI/CD build/push/deploy job; Order's apply job `needs:` Payment's apply job)

## Phase 4 — Fulfillment Service
- [ ] 18. Simulated shipping on OrderConfirmed (publishes OrderShipped, OrderDelivered)
- [ ] 19. Fulfillment Service Dockerfile, Terraform + deploy (`services/fulfillment-service/Dockerfile` + `services/fulfillment-service/infra/terraform/`: Container App image from shared ACR, no DB, subscription to OrderConfirmed, owned topics: OrderShipped/OrderDelivered + Order's new subscriptions to them; path-filtered CI/CD build/push/deploy job; Order's apply job `needs:` Fulfillment's apply job)

**Checkpoint: all four services deployed and wired end-to-end on real Azure infra.**

## Phase 5 — End-to-end verification & docs
- [ ] 20. Integration test suite (happy path, out-of-stock, payment-failed, idempotency re-delivery)
- [ ] 21. README (local dev instructions, link to docs/SPEC.md, one-time bootstrap command from Phase 0 task 4)

## Explicitly not built
`PaymentRefunded` — no topic, subscription, producer, or consumer anywhere
in this plan (refunds/cancellation out of scope per spec).

## Out of scope (per spec)
Cart, Notification, Analytics, Search services; saga orchestration; locking/
TTL reservation semantics; customer-initiated cancellation; refunds;
auth/authz; transport-level retry/DLQ handling.

## Known limitation accepted for MVP simplicity: no transactional outbox
Each service's write path (DB commit, then publish the resulting event) is
not atomic — there is no outbox table, CDC, or two-phase mechanism coupling
the two. If a service commits its state change and then crashes/fails before
the publish call succeeds, the event is never emitted and the order can be
left stuck (e.g. stock decremented but no `InventoryReserved`/`InventoryFailed`
ever published, so Order Service never leaves `CREATED`). This is a known
correctness gap, deliberately deferred to keep Phase 0's messaging
abstraction simple; revisit if this moves beyond MVP.
