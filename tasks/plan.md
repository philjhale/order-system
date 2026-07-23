# Plan: Order System MVP

Source: `docs/SPEC.md` (repo root). Scope: full MVP — Order, Inventory,
Payment, Fulfillment services, per the spec's High-Level Design, Order
Placement Flow, Order State Machine, Events, Tech Stack, and Data Model
sections.

## Project layout decision (deferred to this phase by the spec)

**Single repo, one self-contained folder per service** — each service owns
its own `.sln`, `src/`, `tests/`, Terraform, and `Dockerfile`, so it reads
and builds like a standalone project even though it lives in the monorepo:

```
shared/
  OrderSystem.Contracts/        # shared event DTOs + enums (OrderStatus, PaymentStatus)
  OrderSystem.Messaging/        # IEventPublisher/IEventSubscriber abstraction
                                 #   + Azure Service Bus impl + in-memory impl (for tests/local dev)
services/
  order-service/
    OrderSystem.OrderService.sln
    src/OrderSystem.OrderService/
    tests/OrderSystem.OrderService.Tests/
    infra/terraform/             # Order Container App, Order SQL DB, Order-owned topics
    Dockerfile
  inventory-service/
    OrderSystem.InventoryService.sln
    src/OrderSystem.InventoryService/
    tests/OrderSystem.InventoryService.Tests/
    infra/terraform/             # Inventory Container App, Inventory SQL DB, its topics + subscriptions
    Dockerfile
  payment-service/
    OrderSystem.PaymentService.sln
    src/OrderSystem.PaymentService/
    tests/OrderSystem.PaymentService.Tests/
    infra/terraform/             # Payment Container App, Payment SQL DB, its topics + subscriptions
    Dockerfile
  fulfillment-service/
    OrderSystem.FulfillmentService.sln
    src/OrderSystem.FulfillmentService/
    tests/OrderSystem.FulfillmentService.Tests/
    infra/terraform/             # Fulfillment Container App (no DB), its topics + subscriptions
    Dockerfile
integration-tests/
  OrderSystem.IntegrationTests/  # cross-service, in-memory bus, full flow scenarios
                                 #   (doesn't belong to any one service folder)
infra/
  terraform-bootstrap/    # one-time, local state: remote-state storage account
  terraform/
    shared/               # resource group, Container Apps environment,
                           #   Service Bus namespace, Key Vault, managed identities,
                           #   Azure Container Registry (one, shared by all 4 services)
.github/
  workflows/
```

Each service's `.sln` references `shared/OrderSystem.Contracts` and
`shared/OrderSystem.Messaging` via relative-path project references (e.g.
`../../shared/OrderSystem.Contracts/OrderSystem.Contracts.csproj`) — still
one repo, still trivially in sync (no package feed/versioning to
coordinate), but each service's own `dotnet build`/`dotnet test` only
touches that service's `.sln`, matching the path-filtered per-service CI
workflows and the fact that each service's Terraform, Dockerfile, and code
now live and change together in one folder. There is no root-level
aggregate `.sln` — a developer working on Order Service opens
`services/order-service/OrderSystem.OrderService.sln` and nothing else
builds or tests as a side effect.

Infra is split per-service (not one monolithic config) so each service's
phase can add exactly the infra slice it needs — see "Delivery / branching
strategy" and the incremental topic/subscription wiring under each phase
below. Only the genuinely cross-cutting Terraform (resource group,
Container Apps environment, Service Bus namespace, Key Vault, ACR) stays
under the top-level `infra/terraform/shared/`, since no single service
folder owns it.

Rationale: this trades the convenience of a single root `.sln` for folder
boundaries that mirror how the services actually deploy — independently,
each with its own image and its own Terraform state. Shared event contracts
still live in one place (`shared/`) and are referenced directly rather than
published as a package, so producers/consumers can't drift out of sync —
that directly serves the spec's idempotency/audit NFRs, since a mismatched
event schema between services is exactly the kind of bug those NFRs exist
to prevent.

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
1. **Solution scaffolding** — `shared/OrderSystem.Contracts/` and
   `shared/OrderSystem.Messaging/` project skeletons; each of the 4
   `services/<name>/` folders with its own `.sln`, empty `src/`/`tests/`
   project skeletons, and a relative-path project reference to both shared
   projects; `integration-tests/OrderSystem.IntegrationTests/` skeleton;
   root-level `Directory.Build.props`/`.editorconfig` so shared settings
   (nullable, warnings-as-errors, etc.) apply across every service folder
   without a root `.sln`.
   - *Verify:* `dotnet build`/`dotnet test` succeeds when run from inside
     each of the 4 service folders independently, and from
     `integration-tests/`.
2. **Event contracts** — DTOs for all 11 events in the spec's Events table,
   `OrderStatus`/`PaymentStatus` enums matching Data Model exactly (incl.
   unused `RefundPending`/`Refunded` members, per spec note that these
   exist but are unused this MVP).
   - *Verify:* unit tests roundtrip-(de)serialize every event DTO.
3. **Messaging abstraction** — `IEventPublisher`, `IEventSubscriber`
   (routing/partitioning by `orderId`), Azure Service Bus implementation
   (session id = `orderId`), in-memory implementation for tests/local dev.
   `IEventSubscriber` exposes an explicit abandon/retry outcome (in addition
   to complete/dead-letter) so a handler can signal "not yet processable,
   redeliver later" distinct from "processed" or "poison message" — needed
   because session ordering only holds *within* one subscription, not
   across the separate subscriptions a consumer may have to different
   topics (see task 9).
   - *Verify:* unit tests against the in-memory implementation confirm
     per-`orderId` ordering, and confirm an abandoned message is redelivered
     rather than lost.
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
   itself. Each subsequent service phase adds its own thin workflow file
   (e.g. `order-service-ci.yml`) that's `paths`-filtered to only
   `services/order-service/**` and calls this reusable workflow with that
   service's folder as a parameter — so a change confined to one service
   folder never triggers another service's build/test/deploy.

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
   `OrderCancelled`), `InventoryReleased` → no state transition (order is
   already `CANCELLED` by the time this arrives per the compensation
   path) but still consumed and recorded in `OrderEvents` for audit,
   `OrderShipped` → `SHIPPED`, `OrderDelivered` → `DELIVERED`. Idempotent
   via event-id/current-state check. (Consumer code exists now even
   though the corresponding subscriptions for Inventory/Payment/
   Fulfillment events aren't wired until those services' own phases create
   the topics.)

   Each of these events also has a required *precondition* state (e.g.
   `PaymentCompleted` requires the order to already be `RESERVED`;
   `OrderShipped` requires `CONFIRMED`). Because Inventory/Payment/
   Fulfillment each react to upstream events on their own independent
   subscriptions, there's no cross-service ordering guarantee that Order
   Service's own consumer has caught up before a downstream event arrives
   (e.g. Payment Service can process `InventoryReserved` and publish
   `PaymentCompleted` before Order Service's own `InventoryReserved`
   consumer has moved the order to `RESERVED`). If an event arrives and its
   precondition state doesn't hold yet, the consumer abandons the message
   (via the outcome from task 3) rather than rejecting or dropping it, so
   Service Bus redelivers it once the precondition has had time to land.
   Rely on the subscription's built-in max-delivery-count to dead-letter a
   message that never becomes processable (genuine poison message), rather
   than building custom retry/backoff logic.
   - *Verify:* unit tests confirm re-delivering any event is a no-op on
     second delivery (idempotency NFR); `InventoryReleased` for an already-
     `CANCELLED` order is recorded but doesn't change `Status`; an event
     delivered before its precondition state is reached is abandoned (not
     completed, not dead-lettered) and is processed normally on a
     subsequent (simulated) redelivery.
10. **Order Service Dockerfile, Terraform + deploy** —
    `services/order-service/Dockerfile` (multi-stage; build context is the
    *repo root*, not the service folder, so it can `COPY` the `shared/`
    projects it references); `services/order-service/infra/terraform/`:
    Container App (image pulled from the shared ACR's `order-service`
    repository), Azure SQL Serverless DB, and the topics Order *owns*
    (`OrderCreated`, `OrderCancelled`, `OrderConfirmed`); CI/CD job builds
    the image, pushes to ACR tagged with the commit SHA, then deploys the
    Container App pinned to that tag.
    - *Verify:* `docker build -f services/order-service/Dockerfile .` (run
      from repo root) succeeds locally; `terraform plan` clean inside
      `services/order-service/infra/terraform/`; CI workflow builds,
      pushes, and deploys successfully.

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
14. **Inventory Service Dockerfile, Terraform + deploy** —
    `services/inventory-service/Dockerfile` (same repo-root-build-context
    pattern as Order Service's); `services/inventory-service/infra/terraform/`:
    Container App (image from the shared ACR's `inventory-service`
    repository), Azure SQL Serverless DB, Inventory's subscriptions to
    Order's `OrderCreated` and `OrderCancelled` topics (which now exist as
    of Phase 1), and the topics Inventory *owns* (`InventoryReserved`,
    `InventoryFailed`, `InventoryReleased`); also adds Order Service's
    subscriptions to these three new topics (Order's consumer code from
    Phase 1 task 9 has been waiting for them) as a small addition to
    `services/order-service/infra/terraform/`. CI/CD job builds/pushes the
    image and deploys Inventory Service.
    - *Verify:* `docker build -f services/inventory-service/Dockerfile .`
      succeeds locally; `terraform plan` clean for both service folders'
      Terraform; CI builds, pushes, and deploys; a manually-published
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
17. **Payment Service Dockerfile, Terraform + deploy** —
    `services/payment-service/Dockerfile` (same pattern);
    `services/payment-service/infra/terraform/`: Container App (image from
    the shared ACR's `payment-service` repository), Azure SQL Serverless
    DB, Payment's subscription to Inventory's `InventoryReserved` topic,
    and the topics Payment *owns* (`PaymentCompleted`, `PaymentFailed`);
    also adds Order Service's subscriptions to these two new topics (in
    `services/order-service/infra/terraform/`). CI/CD job builds/pushes the
    image and deploys Payment Service.
    - *Verify:* `docker build -f services/payment-service/Dockerfile .`
      succeeds locally; `terraform plan` clean; CI builds, pushes, and
      deploys; end-to-end through Payment reaches `CONFIRMED` or
      `CANCELLED` correctly on deployed infra.

### Phase 4 — Fulfillment Service
18. **Simulated shipping on `OrderConfirmed`** — publish `OrderShipped`
    then `OrderDelivered` (simulated delay, no carrier integration).
19. **Fulfillment Service Dockerfile, Terraform + deploy** —
    `services/fulfillment-service/Dockerfile` (same pattern);
    `services/fulfillment-service/infra/terraform/`: Container App (image
    from the shared ACR's `fulfillment-service` repository; no DB —
    Fulfillment owns no tables per the spec's Data Model), Fulfillment's
    subscription to Order's `OrderConfirmed` topic (created in Phase 1,
    finally subscribed to here), and the topics Fulfillment *owns*
    (`OrderShipped`, `OrderDelivered`); also adds Order Service's
    subscriptions to these two new topics (in
    `services/order-service/infra/terraform/`). CI/CD job builds/pushes the
    image and deploys Fulfillment Service.
    - *Verify:* `docker build -f services/fulfillment-service/Dockerfile .`
      succeeds locally; `terraform plan` clean; CI builds, pushes, and
      deploys; full flow reaches `SHIPPED` → `DELIVERED` on deployed infra.

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

## Known limitation accepted for MVP simplicity: no transactional outbox
Each service's write path (DB commit, then publish the resulting event) is
not atomic — there is no outbox table, CDC, or two-phase mechanism coupling
the two. If a service commits its state change and then crashes/fails before
the publish call succeeds, the event is never emitted and the order can be
left stuck (e.g. stock decremented but no `InventoryReserved`/`InventoryFailed`
ever published, so Order Service never leaves `CREATED`). This is a known
correctness gap, deliberately deferred to keep Phase 0's messaging
abstraction simple; revisit if this moves beyond MVP.
