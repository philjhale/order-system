# Plan: Order System MVP

Source: `docs/SPEC.md` (repo root). Scope: full MVP — Order, Inventory,
Payment, Fulfillment services, per the spec's High-Level Design, Order
Placement Flow, Order State Machine, Events, Tech Stack, and Data Model
sections.

## Project layout decision (deferred to this phase by the spec)

**Single repo, single .NET solution.** `OrderSystem.sln` at repo root, with:

```
src/
  OrderSystem.Contracts/        # shared event DTOs + enums (OrderStatus, PaymentStatus)
  OrderSystem.Messaging/        # IEventPublisher/IEventSubscriber abstraction
                                 #   + Azure Service Bus impl + in-memory impl (for tests/local dev)
  OrderSystem.OrderService/
    Dockerfile                  # multi-stage build, repo root as build context
  OrderSystem.InventoryService/
    Dockerfile
  OrderSystem.PaymentService/
    Dockerfile
  OrderSystem.FulfillmentService/
    Dockerfile
tests/
  OrderSystem.OrderService.Tests/
  OrderSystem.InventoryService.Tests/
  OrderSystem.PaymentService.Tests/
  OrderSystem.FulfillmentService.Tests/
  OrderSystem.IntegrationTests/  # cross-service, in-memory bus, full flow scenarios
infra/
  terraform-bootstrap/    # one-time, local state: remote-state storage account
  terraform/
    shared/               # resource group, Container Apps environment,
                           #   Service Bus namespace, Key Vault, managed identities,
                           #   Azure Container Registry (one, shared by all 4 services)
    order-service/        # Order Container App, Order SQL DB, Order-owned topics
    inventory-service/    # Inventory Container App, Inventory SQL DB, its topics + subscriptions
    payment-service/      # Payment Container App, Payment SQL DB, its topics + subscriptions
    fulfillment-service/  # Fulfillment Container App (no DB), its topics + subscriptions
.github/
  workflows/
```

Infra is split per-service (not one monolithic config) so each service's
phase can add exactly the infra slice it needs — see "Delivery / branching
strategy" and the incremental topic/subscription wiring under each phase
below.

Rationale: 4 services + 1 shared contracts/messaging library is small enough
that multi-repo overhead (cross-repo versioning, coordinated PRs for shared
contract changes) isn't worth it for a demo project. A single solution lets
`dotnet build`/`dotnet test` cover everything in one command and keeps
shared event contracts trivially in sync across producers/consumers — this
directly serves the spec's idempotency/audit NFRs, since a mismatched event
schema between services is exactly the kind of bug those NFRs exist to
prevent.

The in-memory `IEventPublisher`/`IEventSubscriber` implementation is not
named in the spec's Tech Stack (which pins Azure Service Bus for
production) — it exists purely so unit/integration tests and local `dotnet
run` don't require a live Azure Service Bus namespace. Production wiring
always uses the Service Bus implementation.

## Delivery / branching strategy

**Each phase (0–5) ships as its own PR directly into `main`** — no single
big PR at the end, and no long-lived integration branch spanning the whole
feature. Concretely:

- For each phase: branch off the current tip of `main` (e.g.
  `feature/order-system-mvp-phase-0`, `-phase-1`, ... matching the phase
  numbers above), implement only that phase's task(s), review, open a PR
  into `main`, and get it merged before branching the next phase — the next
  phase's branch is cut from `main` *after* the previous phase's PR has
  landed, so it always includes the previous phase's code. This is
  trunk-based delivery applied at phase granularity instead of
  whole-feature granularity.
- Multi-task phases (e.g. Phase 0's three tasks, Phase 1's three tasks)
  land as separate commits within that phase's single PR — one commit per
  task, one PR per phase — matching the "atomic commits, small PRs" guidance
  already in `git-workflow-and-versioning`.
- This `feature/order-system-mvp` branch/worktree carries the planning
  artifacts (`tasks/plan.md`, `tasks/todo.md`, `tasks/feature-stage.md`)
  and is itself merged into `main` via its own PR *before* Phase 0 starts —
  so `main` carries the plan as part of its history. Each subsequent phase
  branch is then cut from `main` (which already includes this plan), gets
  its own PR, and merges independently; the plan isn't re-merged with each
  phase.
- `/agent-skills:review` runs against each phase's diff before its PR is
  opened. `/agent-skills:ship`'s full go/no-go checklist runs once, after
  the last phase (5) has merged, as a final check on the assembled system
  on `main` — not repeated for every phase.
- The stage tracker (`tasks/feature-stage.md`) checks off "build" only once
  all 6 phase PRs (0–5) have merged; the "ship" stage then covers the final
  system-wide checklist described above.
- Each service phase (1–4) leaves the system in a real, deployed state on
  Azure by the time its PR merges — infra and CI/CD land incrementally
  with the code, not as a separate phase at the end.

## Dependency graph

```
Contracts ──┬─▶ Messaging ──▶ OrderService ──┬─▶ InventoryService ──┬─▶ PaymentService ──┬─▶ FulfillmentService ──▶ IntegrationTests ──▶ Docs
            │                (owns topics:    │  (subscribes to:     │  (subscribes to:    │  (subscribes to:
            │                 OrderCreated,    │   OrderCreated,      │   InventoryReserved; │   OrderConfirmed;
            │                 OrderCancelled,  │   OrderCancelled;    │   owns topics:       │   owns topics:
            │                 OrderConfirmed)  │   owns topics:       │   PaymentCompleted,  │   OrderShipped,
            │                                  │   InventoryReserved, │   PaymentFailed)     │   OrderDelivered)
            │                                  │   InventoryFailed,   │
            │                                  │   InventoryReleased) │
            └──────────────────────────────────┴──────────────────────┴─────────────────────┴────────────────────────▶ (all)

Shared Terraform foundation (resource group, Container Apps environment,
Service Bus namespace, Key Vault, managed identities) sits under Phase 0 —
every service's own Terraform module depends on it. Each service phase then
adds: its own Container App + SQL DB (if any) + the topics it *owns*, plus
subscriptions *to* topics that already exist by that point. Because Order
Service is both first-built and a consumer of every other service's events,
its consumer code is written in Phase 1 but the actual Service Bus
subscriptions wiring those events to Order Service are added incrementally
in Phases 2–4, at the point each producing service's topic first exists.
```

Each service task below is vertically sliced (domain + persistence +
API/consumer + its own publishes + its own infra + its own deploy step), so
each phase leaves the system in a real, deployed, independently-verifiable
state before the next phase starts.

## Phases & Tasks

### Phase 0 — Shared foundation
1. **Solution scaffolding** — `OrderSystem.sln`, project skeletons per the
   layout above, `Directory.Build.props`/`.editorconfig` for shared
   settings, solution-level `dotnet test` wired up.
   - *Verify:* `dotnet build` succeeds with all empty projects.
2. **Event contracts** — DTOs for all 11 events in the spec's Events table,
   `OrderStatus`/`PaymentStatus` enums matching Data Model exactly (incl.
   unused `RefundPending`/`Refunded` members, per spec note that these
   exist but are unused this MVP).
   - *Verify:* unit tests roundtrip-(de)serialize every event DTO.
3. **Messaging abstraction** — `IEventPublisher`, `IEventSubscriber`
   (routing/partitioning by `orderId`), Azure Service Bus implementation
   (session id = `orderId`), in-memory implementation for tests/local dev.
   - *Verify:* unit tests against the in-memory implementation confirm
     per-`orderId` ordering.
4. **Terraform remote state bootstrap** — `infra/terraform-bootstrap/`
   (local state, run once by hand, not in CI): Azure Storage Account +
   blob container used as the `azurerm` backend for every config below.
   Can't live in the main config because that config needs the backend to
   already exist before it can use it. Document the one-time manual
   `terraform apply` command in the README (Phase 5).
   - *Verify:* `terraform validate` clean; storage account + container
     exist after a manual apply.
5. **Terraform shared foundation** — `infra/terraform/shared/` (azurerm
   backend from #4): resource group, Container Apps environment, Service
   Bus namespace (no topics yet — each service phase below adds its own),
   Key Vault, managed identities, and one Azure Container Registry (ACR)
   shared by all 4 services (one registry, one set of push/pull
   credentials via managed identity — each service gets its own repository
   *within* that registry, e.g. `order-service`, `inventory-service`).
   Every per-service Terraform module below references this via remote
   state / data sources.
   - *Verify:* `terraform validate` + `terraform plan` clean; ACR exists
     and Container Apps environment's managed identity has `AcrPull`.
6. **CI/CD skeleton** — reusable GitHub Actions workflow (build + test on
   PR, `docker build`/`docker push` to the shared ACR, `terraform
   plan`/`apply` job), plus the `terraform plan`/`apply` job for `shared/`
   itself — this reusable workflow (parameterized by service name/path) is
   the pattern each subsequent service phase's own workflow job calls into.

### Phase 1 — Order Service (core; nothing else can be tested end-to-end without it)
7. **Order domain + persistence** — `Orders`/`OrderItems`/`OrderEvents`
   tables (EF Core, Azure SQL), order state machine enforcing only the
   transitions in the spec's state table, append-only `OrderEvents` write
   on every transition.
   - *Verify:* unit tests cover every legal transition and reject illegal
     ones (e.g. `CREATED → CONFIRMED` directly).
8. **Order Service HTTP API** — `POST /orders` (create, state `CREATED`,
   publish `OrderCreated`), `GET /orders/{id}` (status query).
   - *Verify:* integration test hitting the API against a test DB and
     in-memory bus confirms `OrderCreated` is published with correct
     payload.
9. **Order Service event consumers** — `InventoryReserved` → `RESERVED`,
   `InventoryFailed` → `CANCELLED`, `PaymentCompleted` → `CONFIRMED` (+
   publish `OrderConfirmed`), `PaymentFailed` → `CANCELLED` (+ publish
   `OrderCancelled`), `OrderShipped` → `SHIPPED`, `OrderDelivered` →
   `DELIVERED`. Idempotent via event-id/current-state check. (Consumer
   code exists now even though the corresponding subscriptions for
   Inventory/Payment/Fulfillment events aren't wired until those services'
   own phases create the topics.)
   - *Verify:* unit tests confirm re-delivering any event is a no-op on
     second delivery (idempotency NFR).
10. **Order Service Dockerfile, Terraform + deploy** — multi-stage
    `Dockerfile` under `OrderSystem.OrderService/` (repo root as build
    context, so it can restore the `Contracts`/`Messaging` project
    references); `infra/terraform/order-service/`: Container App (image
    pulled from the shared ACR's `order-service` repository), Azure SQL
    Serverless DB, and the topics Order *owns* (`OrderCreated`,
    `OrderCancelled`, `OrderConfirmed`); CI/CD job builds the image, pushes
    to ACR tagged with the commit SHA, then deploys the Container App
    pinned to that tag.
    - *Verify:* `docker build` succeeds locally; `terraform plan` clean; CI
      workflow builds, pushes, and deploys successfully.

**Checkpoint:** Order Service is deployed and fully testable (API +
consumers) standalone, with hand-published inventory/payment events (via
the in-memory bus in tests) standing in for the other services. Confirm
before starting Phase 2.

### Phase 2 — Inventory Service
11. **Inventory domain + persistence** — `InventoryItems` table.
12. **Reserve on `OrderCreated`** — all-or-nothing reservation across every
    order line; publish `InventoryReserved` or `InventoryFailed { reason:
    OutOfStock }`; idempotent (re-delivery doesn't double-decrement).
    - *Verify:* unit test — order with one out-of-stock item reserves
      nothing and publishes `InventoryFailed`; concurrent orders for the
      same last unit never both succeed (NFR: no overselling).
13. **Release on `OrderCancelled`** — release previously-reserved stock,
    publish `InventoryReleased`; no-ops if no reservation exists for that
    order (idempotent, and safe against `OrderCancelled` arriving for an
    order that was never reserved, i.e. the `InventoryFailed` path).
14. **Inventory Service Dockerfile, Terraform + deploy** — multi-stage
    `Dockerfile` under `OrderSystem.InventoryService/` (same pattern as
    Order Service's); `infra/terraform/inventory-service/`: Container App
    (image from the shared ACR's `inventory-service` repository), Azure SQL
    Serverless DB, Inventory's subscriptions to Order's `OrderCreated` and
    `OrderCancelled` topics (which now exist as of Phase 1), and the
    topics Inventory *owns* (`InventoryReserved`, `InventoryFailed`,
    `InventoryReleased`); also adds Order Service's subscriptions to these
    three new topics (Order's consumer code from Phase 1 task 9 has been
    waiting for them) as a small addition to `order-service/`'s Terraform.
    CI/CD job builds/pushes the image and deploys Inventory Service.
    - *Verify:* `docker build` succeeds locally; `terraform plan` clean for
      both modules; CI builds, pushes, and deploys; a manually-published
      `OrderCreated` reaches deployed Inventory and a resulting
      `InventoryReserved`/`InventoryFailed` reaches deployed Order Service.

### Phase 3 — Payment Service
15. **Payment domain + persistence** — `Payments` table.
16. **Charge on `InventoryReserved`** — simulated payment gateway call
    keyed by `orderId` as idempotency key; publish `PaymentCompleted` or
    `PaymentFailed`.
    - *Verify:* unit test — re-delivering `InventoryReserved` for an
      already-charged order does not double-charge (idempotency NFR +
      "no uncharged confirmation" NFR).
17. **Payment Service Dockerfile, Terraform + deploy** — multi-stage
    `Dockerfile` under `OrderSystem.PaymentService/` (same pattern);
    `infra/terraform/payment-service/`: Container App (image from the
    shared ACR's `payment-service` repository), Azure SQL Serverless DB,
    Payment's subscription to Inventory's `InventoryReserved` topic, and
    the topics Payment *owns* (`PaymentCompleted`, `PaymentFailed`); also
    adds Order Service's subscriptions to these two new topics. CI/CD job
    builds/pushes the image and deploys Payment Service.
    - *Verify:* `docker build` succeeds locally; `terraform plan` clean; CI
      builds, pushes, and deploys; end-to-end through Payment reaches
      `CONFIRMED` or `CANCELLED` correctly on deployed infra.

### Phase 4 — Fulfillment Service
18. **Simulated shipping on `OrderConfirmed`** — publish `OrderShipped`
    then `OrderDelivered` (simulated delay, no carrier integration).
19. **Fulfillment Service Dockerfile, Terraform + deploy** — multi-stage
    `Dockerfile` under `OrderSystem.FulfillmentService/` (same pattern);
    `infra/terraform/fulfillment-service/`: Container App (image from the
    shared ACR's `fulfillment-service` repository; no DB — Fulfillment owns
    no tables per the spec's Data Model), Fulfillment's subscription to
    Order's `OrderConfirmed` topic (created in Phase 1, finally subscribed
    to here), and the topics Fulfillment *owns* (`OrderShipped`,
    `OrderDelivered`); also adds Order Service's subscriptions to these two
    new topics. CI/CD job builds/pushes the image and deploys Fulfillment
    Service.
    - *Verify:* `docker build` succeeds locally; `terraform plan` clean; CI
      builds, pushes, and deploys; full flow reaches `SHIPPED` →
      `DELIVERED` on deployed infra.

**Checkpoint:** all four services exist, deployed, and wired end-to-end on
real Azure infra. Confirm before the final verification/docs phase.

### Phase 5 — End-to-end verification & docs
20. **Integration test suite** (in-memory bus for CI speed, or against the
    real deployed environment for a manual smoke pass) covering the three
    flows in the spec's Success Criteria:
    - sufficient stock + successful charge → `CONFIRMED` → `SHIPPED` →
      `DELIVERED`.
    - insufficient stock → `CANCELLED`, no payment attempted.
    - sufficient stock + failed charge → `CANCELLED`, inventory released.
    - re-delivery of any event in any of the above flows does not change
      the end state.
21. **README** — local dev instructions (run with in-memory bus, no Azure
    dependency needed), pointer to `docs/SPEC.md` for architecture, and the
    one-time manual bootstrap command from Phase 0 task 4 for anyone
    standing up a fresh environment.

## PaymentRefunded — explicitly not built
No topic, subscription, or producer/consumer code for `PaymentRefunded` is
created anywhere above — the spec defines it for completeness only; no MVP
flow produces it (refunds/cancellation are out of scope).

## Out of scope (per spec)
Cart, Notification, Analytics, Search services; saga orchestration; locking/
TTL reservation semantics; customer-initiated cancellation; refunds;
auth/authz; transport-level retry/DLQ handling.
