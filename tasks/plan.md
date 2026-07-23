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
  OrderSystem.InventoryService/
  OrderSystem.PaymentService/
  OrderSystem.FulfillmentService/
tests/
  OrderSystem.OrderService.Tests/
  OrderSystem.InventoryService.Tests/
  OrderSystem.PaymentService.Tests/
  OrderSystem.FulfillmentService.Tests/
  OrderSystem.IntegrationTests/  # cross-service, in-memory bus, full flow scenarios
infra/
  terraform/
.github/
  workflows/
```

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

**Each phase (0–8) ships as its own PR directly into `main`** — no single
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
- This `feature/order-system-mvp` branch/worktree is *not* one of the
  phase branches and is never merged into `main` itself — it exists only to
  hold this orchestration bookkeeping (`tasks/plan.md`, `tasks/todo.md`,
  `tasks/feature-stage.md`) across the whole multi-phase effort. It gets
  discarded once all phases are merged.
- `/agent-skills:review` runs against each phase's diff before its PR is
  opened. `/agent-skills:ship`'s full go/no-go checklist runs once, after
  the last phase (8) has merged, as a final check on the assembled system
  on `main` — not repeated for every phase.
- The stage tracker (`tasks/feature-stage.md`) checks off "build" only once
  all 8 phase PRs have merged; the "ship" stage then covers the final
  system-wide checklist described above.

## Dependency graph

```
Contracts ──┬─▶ Messaging ──┬─▶ OrderService ──┬─▶ IntegrationTests
            │                │                  │
            │                ├─▶ InventoryService┤
            │                │                  │
            │                ├─▶ PaymentService ─┤
            │                │                  │
            │                └─▶ FulfillmentService
            │
            └────────────────────────────────────▶ (all services + tests)

Terraform / CI-CD: independent of app code, can proceed in parallel once
service container images exist to reference (deploy steps only meaningful
after Phase 3).
```

Each service task below is vertically sliced (domain + persistence + API/consumer
+ its own publishes), so each is independently testable against an
in-memory bus before the next service is built.

## Phases & Tasks

### Phase 0 — Shared foundation
1. **Solution scaffolding** — `OrderSystem.sln`, project skeletons per the
   layout above, `Directory.Build.props`/`.editorconfig` for shared
   settings, solution-level `dotnet test` wired up.
   - *Verify:* `dotnet build` succeeds with all empty projects.
2. **Event contracts** — DTOs for all 11 events in the spec's Events table,
   `OrderStatus`/`PaymentStatus` enums matching Data Model exactly (incl.
   unused `RefundPending`/`Refunded`/`Refunded` members, per spec note that
   these exist but are unused this MVP).
   - *Verify:* unit tests roundtrip-(de)serialize every event DTO.
3. **Messaging abstraction** — `IEventPublisher`, `IEventSubscriber`
   (routing/partitioning by `orderId`), Azure Service Bus implementation
   (session id = `orderId`), in-memory implementation for tests/local dev.
   - *Verify:* unit tests against the in-memory implementation confirm
     per-`orderId` ordering.

### Phase 1 — Order Service (core; nothing else can be tested end-to-end without it)
4. **Order domain + persistence** — `Orders`/`OrderItems`/`OrderEvents`
   tables (EF Core, Azure SQL), order state machine enforcing only the
   transitions in the spec's state table, append-only `OrderEvents` write
   on every transition.
   - *Verify:* unit tests cover every legal transition and reject illegal
     ones (e.g. `CREATED → CONFIRMED` directly).
5. **Order Service HTTP API** — `POST /orders` (create, state `CREATED`,
   publish `OrderCreated`), `GET /orders/{id}` (status query).
   - *Verify:* integration test hitting the API against a test DB and
     in-memory bus confirms `OrderCreated` is published with correct
     payload.
6. **Order Service event consumers** — `InventoryReserved` → `RESERVED`,
   `InventoryFailed` → `CANCELLED`, `PaymentCompleted` → `CONFIRMED` (+
   publish `OrderConfirmed`), `PaymentFailed` → `CANCELLED` (+ publish
   `OrderCancelled`), `OrderShipped` → `SHIPPED`, `OrderDelivered` →
   `DELIVERED`. Idempotent via event-id/current-state check.
   - *Verify:* unit tests confirm re-delivering any event is a no-op on
     second delivery (idempotency NFR).

**Checkpoint:** Order Service alone is fully testable (API + consumers) via
the in-memory bus with hand-published inventory/payment events standing in
for the other services. Confirm before starting Phase 2.

### Phase 2 — Inventory Service
7. **Inventory domain + persistence** — `InventoryItems` table.
8. **Reserve on `OrderCreated`** — all-or-nothing reservation across every
   order line; publish `InventoryReserved` or `InventoryFailed { reason:
   OutOfStock }`; idempotent (re-delivery doesn't double-decrement).
   - *Verify:* unit test — order with one out-of-stock item reserves
     nothing and publishes `InventoryFailed`; concurrent orders for the
     same last unit never both succeed (NFR: no overselling).
9. **Release on `OrderCancelled`** — release previously-reserved stock,
   publish `InventoryReleased`; no-ops if no reservation exists for that
   order (idempotent, and safe against `OrderCancelled` arriving for an
   order that was never reserved, i.e. the `InventoryFailed` path).

### Phase 3 — Payment Service
10. **Payment domain + persistence** — `Payments` table.
11. **Charge on `InventoryReserved`** — simulated payment gateway call
    keyed by `orderId` as idempotency key; publish `PaymentCompleted` or
    `PaymentFailed`.
    - *Verify:* unit test — re-delivering `InventoryReserved` for an
      already-charged order does not double-charge (idempotency NFR +
      "no uncharged confirmation" NFR).

### Phase 4 — Fulfillment Service
12. **Simulated shipping on `OrderConfirmed`** — publish `OrderShipped`
    then `OrderDelivered` (simulated delay, no carrier integration).

**Checkpoint:** all four services exist. Confirm before integration-testing
the full flow.

### Phase 5 — End-to-end verification
13. **Integration test suite** (in-memory bus, real per-service DBs or
    test containers) covering the three flows in the spec's Success
    Criteria:
    - sufficient stock + successful charge → `CONFIRMED` → `SHIPPED` →
      `DELIVERED`.
    - insufficient stock → `CANCELLED`, no payment attempted.
    - sufficient stock + failed charge → `CANCELLED`, inventory released.
    - re-delivery of any event in any of the above flows does not change
      the end state.

### Phase 6 — Infrastructure as code
14a. **Terraform remote state bootstrap** — a small, separate
    `infra/terraform-bootstrap/` config (local state, run once by hand, not
    in CI) that provisions only the Azure Storage Account + blob container
    used as the `azurerm` backend for the main config below. This can't
    live in the main config because that config needs the backend to
    already exist before it can use it. Document the one-time manual
    `terraform apply` command in the README (Phase 8) since it's outside
    the normal CI/CD path.
    - *Verify:* `terraform validate` clean; storage account + container
      exist after a manual apply.
14b. **Terraform (main)** — `infra/terraform/` configured with an
    `azurerm` backend (storage account + container from 14a, providing
    state locking via blob lease), provisioning: resource group, Container
    Apps environment + one app/deployment per service, Service Bus
    namespace + topic/subscription per event type, Azure SQL Serverless
    instance per Order/Inventory/Payment (Fulfillment owns no tables per
    the spec's Data Model), managed identities, Key Vault for secrets.
    - *Verify:* `terraform validate` + `terraform plan` clean against the
      remote backend from 14a.

### Phase 7 — CI/CD
15. **GitHub Actions** — build + test on PR; container image build; deploy
    workflow (manual-trigger acceptable for a demo, given no uptime/scale
    NFRs) running `terraform plan`/`apply` against the Phase 14a remote
    backend — CI never runs the bootstrap config, only the main one.

### Phase 8 — Docs
16. **README** — local dev instructions (run with in-memory bus, no Azure
    dependency needed), pointer to `docs/SPEC.md` for architecture, and the
    one-time manual bootstrap command from 14a for anyone standing up a
    fresh environment.

## Out of scope (per spec)
Cart, Notification, Analytics, Search services; saga orchestration; locking/
TTL reservation semantics; customer-initiated cancellation; refunds;
auth/authz; transport-level retry/DLQ handling.
