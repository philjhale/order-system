# Todo: Order System MVP

Each numbered task = its own branch off `main` + its own PR (phases are
grouping labels and checkpoints, not PR boundaries). See `tasks/plan.md`
"Delivery / branching strategy" for details.

**Generation time tracking:** `/agent-skills:build` for each task is
followed by `/agent-skills:review`, with any Critical findings fixed
before the task is marked done. Each completed task below records the
*total* wall-clock time for that whole cycle — build, review, and
critical-finding fixes together, start to finish. Recorded live
(start/end noted during the session), starting from task 2; task 1
predates this convention and is marked "not tracked."

**Total generation time so far:** not tracked (task 1 only)

## Phase 0 — Shared foundation
- [x] 1. Solution scaffolding (`shared/` Contracts+Messaging skeletons, each with its own `tests/` project + a `shared/OrderSystem.Shared.sln` so they build/test standalone; per-service `services/<name>/` folders each with own `.sln`, `src/`, `tests/`, referencing `shared/` by relative path; `integration-tests/` skeleton; root build props) — generation time: not tracked
- [ ] 2. Event contracts (DTOs for all 11 events, OrderStatus/PaymentStatus enums)
- [ ] 3. Messaging abstraction (IEventPublisher/IEventSubscriber w/ explicit abandon-for-redelivery outcome, Service Bus + in-memory impls; MaxDeliveryCount=10 + scheduled-redelivery delay so a short cross-topic race isn't dead-lettered)
- [ ] 4. Azure account configuration (confirm/select subscription, register required resource providers: Microsoft.App/ServiceBus/Sql/ContainerRegistry/Storage) + Terraform remote state bootstrap (storage account + container, local state, one-time manual apply). Also do task 6's app-registration + OIDC-credential bootstrap here, ahead of task-number order — task 5's AAD group membership needs its object id.
- [ ] 5. Terraform shared foundation (resource group, Container Apps environment, Service Bus namespace at Standard SKU (mandatory — Basic has no topics/subscriptions or sessions), shared user-assigned identity for ACR pulls (granted AcrPull on the registry, attached per-Container-App in later tasks — ACR auth is per-app, not environment-wide), shared Azure Container Registry, AAD group as SQL AAD-admin (CI app registration added as a member) — passwordless data-plane auth for Service Bus + SQL replaces Key Vault entirely, so no Key Vault is provisioned)
- [ ] 6. CI/CD skeleton (Azure AD app with two federated OIDC credentials — PR-triggered subject + main-branch subject, so every task's PR CI authenticates, not just one branch — granted Contributor + User Access Administrator at subscription scope (not resource-group scope, since task 5's Terraform is what creates that group; Contributor alone can't create role assignments), IDs stored as repo secrets; single top-level orchestrating workflow (not one independent file per service — `needs:` can't cross separate workflow runs) that always builds/tests `shared/` and calls each affected service's reusable build/test/docker-build-push/terraform-plan-apply workflow as a job in the same run; a `shared/**`-only change is treated as affecting all 4 services (forces every service job to run, not none, since every service embeds `shared/` by reference) rather than being silently skipped by the per-service path filter; when a PR touches two service Terraform folders, subscribing service's apply job declares `needs:` on topic-owning service's apply job, now enforceable since both jobs share one run)

## Phase 1 — Order Service
- [ ] 7. Order domain + persistence (Orders/OrderItems/OrderEvents, state machine; DbContext uses EnableRetryOnFailure() for Azure SQL Serverless auto-pause resilience)
- [ ] 8. Order Service HTTP API (POST /orders, GET /orders/{id})
- [ ] 9. Order Service event consumers (InventoryReserved/InventoryFailed/PaymentCompleted/PaymentFailed/OrderShipped/OrderDelivered → state transitions; InventoryReleased consumed + audit-logged, no transition; events arriving before their precondition state is reached are abandoned for redelivery, not rejected/dropped — relies on subscription max-delivery-count for poison messages)
- [ ] 10. Order Service Dockerfile, Terraform + deploy (`services/order-service/Dockerfile` + `services/order-service/infra/terraform/`: own managed identity granted Azure Service Bus Data Owner on the namespace; SQL DB with Azure-AD-only auth + firewall rule allowing Azure services; Container App image from shared ACR, both ACR-pull and own identity attached, min_replicas=1 (Order Service also consumes Service Bus events and has no HTTP-driven scale trigger between calls); owned topics: OrderCreated/OrderCancelled/OrderConfirmed; path-filtered CI/CD build/push/deploy job; a `azurerm_container_app_job` (not the GitHub runner, which can't reach SQL) first creates the contained DB user via a CI-fetched short-lived AAD token, then runs retry-wrapped EF Core migrations, before the Container App revision goes live)

**Checkpoint: Order Service deployed and testable standalone.**

## Phase 2 — Inventory Service
- [ ] 11. Inventory domain + persistence (InventoryItems + InventoryReservations — per-order/product record for exact-quantity release, deleted on release + InventoryOrderOutcomes — permanent, never-deleted per-order idempotency record, since InventoryReservations alone can't detect an already-processed order once its reservation is released; DbContext uses EnableRetryOnFailure())
- [ ] 12. Reserve on OrderCreated (checks InventoryOrderOutcomes first for idempotency — not InventoryReservations, which is deleted on release and would let a late redelivery re-reserve a terminally-cancelled order; atomic conditional decrement per item to prevent overselling; writes InventoryReservations row per item + InventoryOrderOutcomes row; publishes InventoryReserved/InventoryFailed, passing totalAmount/paymentMethod through unchanged so Payment Service has something to charge)
- [ ] 13. Release on OrderCancelled (reads InventoryReservations for exact quantities, restores stock, deletes reservation rows, publishes InventoryReleased; InventoryOrderOutcomes row is kept permanently, not deleted)
- [ ] 14. Inventory Service Dockerfile, Terraform + deploy (`services/inventory-service/Dockerfile` + `services/inventory-service/infra/terraform/`: same per-service identity/Service Bus RBAC/SQL AAD-auth+firewall pattern as task 10; Container App image from shared ACR, both identities attached, min_replicas=1 (no HTTP traffic to scale from zero on), subscriptions to OrderCreated/OrderCancelled, owned topics: InventoryReserved/InventoryFailed/InventoryReleased + Order's new subscriptions to them; path-filtered CI/CD build/push/deploy job runs the in-Azure migration job (contained-user creation + retry-wrapped EF Core migrations) before Container App revision goes live; Order's apply job `needs:` Inventory's apply job)

## Phase 3 — Payment Service
- [ ] 15. Payment domain + persistence (Payments; OrderId is a plain column, not a DB-level FK — Orders lives in a different service's database; DbContext uses EnableRetryOnFailure())
- [ ] 16. Charge on InventoryReserved (charges totalAmount/paymentMethod read from the event payload; idempotent; publishes PaymentCompleted/PaymentFailed)
- [ ] 17. Payment Service Dockerfile, Terraform + deploy (`services/payment-service/Dockerfile` + `services/payment-service/infra/terraform/`: same per-service identity/Service Bus RBAC/SQL AAD-auth+firewall pattern as task 10; Container App image from shared ACR, both identities attached, min_replicas=1, subscription to InventoryReserved, owned topics: PaymentCompleted/PaymentFailed + Order's new subscriptions to them; path-filtered CI/CD build/push/deploy job runs the in-Azure migration job (contained-user creation + retry-wrapped EF Core migrations) before Container App revision goes live; Order's apply job `needs:` Payment's apply job)

## Phase 4 — Fulfillment Service
- [ ] 18. Simulated shipping on OrderConfirmed (publishes OrderShipped, OrderDelivered)
- [ ] 19. Fulfillment Service Dockerfile, Terraform + deploy (`services/fulfillment-service/Dockerfile` + `services/fulfillment-service/infra/terraform/`: own managed identity granted Azure Service Bus Data Owner on the namespace (no SQL — no DB); Container App image from shared ACR, both identities attached, min_replicas=1, no DB, subscription to OrderConfirmed, owned topics: OrderShipped/OrderDelivered + Order's new subscriptions to them; path-filtered CI/CD build/push/deploy job; Order's apply job `needs:` Fulfillment's apply job)

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
