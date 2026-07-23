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
                           #   Service Bus namespace, shared ACR-pull managed identity,
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
Container Apps environment, Service Bus namespace, ACR) stays under the
top-level `infra/terraform/shared/`, since no single service folder
owns it. Per-service data-plane identities (Service Bus RBAC, SQL AAD
auth) live in each service's own Terraform instead (task 5), since
they're scoped to one service, not cross-cutting.

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

**Each numbered task (1–21) ships as its own PR directly into `main`** — no
single big PR per phase, let alone at the end, and no long-lived
integration branch spanning the whole feature. A phase (0–5) is a grouping
label for related tasks and a checkpoint, not a PR boundary — bundling an
entire phase's tasks into one PR is too large a diff to review or bisect
safely (e.g. Phase 0's four tasks span solution scaffolding, event
contracts, the messaging abstraction, and Azure/Terraform bootstrap —
unrelated concerns that should merge independently). Concretely:

- For each task: branch off the current tip of `main` (e.g.
  `feature/order-system-mvp-task-1`, `-task-2`, ... matching the task
  numbers above), implement only that task, review, open a PR into `main`,
  and get it merged before branching the next task — the next task's branch
  is cut from `main` *after* the previous task's PR has landed, so it
  always includes every prior task's code. This is trunk-based delivery
  applied at task granularity instead of phase or whole-feature granularity.
  A task's PR is typically a single commit; only split into multiple
  commits if the task itself has genuinely separable sub-steps worth
  reviewing independently.
- A task whose work spans two service folders at once (e.g. task 14 also
  adds Order Service's subscriptions to Inventory's new topics, per task 6's
  `needs:` wiring) still ships as that one task's single PR — the PR
  boundary is "one task," not "one service folder."
- This `feature/order-system-mvp` branch/worktree carries the planning
  artifacts (`tasks/plan.md`, `tasks/todo.md`, `tasks/feature-stage.md`)
  and is itself merged into `main` via its own PR *before* task 1 starts —
  so `main` carries the plan as part of its history. Each subsequent task
  branch is then cut from `main` (which already includes this plan), gets
  its own PR, and merges independently; the plan isn't re-merged with each
  task.
- `/agent-skills:review` runs against each task's diff before its PR is
  opened. `/agent-skills:ship`'s full go/no-go checklist runs once, after
  the last task (21) has merged, as a final check on the assembled system
  on `main` — not repeated for every task.
- The stage tracker (`tasks/feature-stage.md`) checks off "build" only once
  all 21 task PRs have merged; the "ship" stage then covers the final
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
Service Bus namespace, shared ACR-pull managed identity) sits under Phase 0 —
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
   `shared/OrderSystem.Messaging/` project skeletons, each with its own
   `tests/` sibling (`shared/OrderSystem.Contracts.Tests/`,
   `shared/OrderSystem.Messaging.Tests/`) and a small `shared/OrderSystem.Shared.sln`
   so they're buildable/testable on their own, not just transitively through
   a service folder — tasks 2 and 3's unit tests live here; each of the 4
   `services/<name>/` folders with its own `.sln`, empty `src/`/`tests/`
   project skeletons, and a relative-path project reference to both shared
   projects; `integration-tests/OrderSystem.IntegrationTests/` skeleton;
   root-level `Directory.Build.props`/`.editorconfig` so shared settings
   (nullable, warnings-as-errors, etc.) apply across every service folder
   without a root `.sln`.
   - *Verify:* `dotnet build`/`dotnet test` succeeds when run from inside
     each of the 4 service folders independently, from
     `shared/OrderSystem.Shared.sln`, and from `integration-tests/`.
2. **Event contracts** — DTOs for all 11 events in the spec's Events table,
   `OrderStatus`/`PaymentStatus` enums matching Data Model exactly (incl.
   unused `RefundPending`/`Refunded` members, per spec note that these
   exist but are unused this MVP).
   - *Verify:* unit tests roundtrip-(de)serialize every event DTO.
3. **Messaging abstraction** — `IEventPublisher`, `IEventSubscriber`
   (routing/partitioning by `orderId`), Azure Service Bus implementation
   (session id = `orderId`) authenticating via `DefaultAzureCredential`
   against the namespace's `.servicebus.windows.net` hostname — passwordless,
   no connection string/secret; the identity it resolves at runtime is each
   service's own user-assigned managed identity (task 5), which must hold
   Service Bus data-plane RBAC on the namespace (wired per-service in tasks
   10/14/17/19). In-memory implementation for tests/local dev.
   `IEventSubscriber` exposes an explicit abandon/retry outcome (in addition
   to complete/dead-letter) so a handler can signal "not yet processable,
   redeliver later" distinct from "processed" or "poison message" — needed
   because session ordering only holds *within* one subscription, not
   across the separate subscriptions a consumer may have to different
   topics (see task 9). Subscriptions set a deliberately generous
   `MaxDeliveryCount` (10) so a short (sub-second) cross-topic race doesn't
   exhaust delivery attempts and get dead-lettered before the precondition
   lands; abandoned messages are re-enqueued a few seconds in the future
   (`ScheduledEnqueueTimeUtc`) rather than being instantly redelivered, so a
   precondition-not-met loop doesn't spin tight.
   - *Verify:* unit tests against the in-memory implementation confirm
     per-`orderId` ordering, and confirm an abandoned message is redelivered
     (after its scheduled delay) rather than lost.
4. **Azure account configuration + Terraform remote state bootstrap** —
   first, a one-time manual precondition that nothing else in this plan can
   run without: confirm/select the target Azure subscription (`az login`,
   `az account set --subscription <id>`), and register the resource
   providers this MVP needs (`az provider register --namespace` for
   `Microsoft.App`, `Microsoft.ServiceBus`, `Microsoft.Sql`,
   `Microsoft.ContainerRegistry`, `Microsoft.Storage`)
   — on a subscription that's never used these services, `terraform apply`
   fails with `MissingSubscriptionRegistration` until this is done, and
   registration can take several minutes to complete. Then:
   `infra/terraform-bootstrap/` (local state, run once by hand, not in CI):
   Azure Storage Account + blob container used as the `azurerm` backend for
   every config below. Can't live in the main config because that config
   needs the backend to already exist before it can use it. Document both
   the provider-registration commands and the one-time manual `terraform
   apply` command in the README (Phase 5).
   - *Verify:* `az provider show --namespace <ns> --query
     registrationState` returns `Registered` for each namespace above;
     `terraform validate` clean; storage account + container exist after a
     manual apply.
5. **Terraform shared foundation** — `infra/terraform/shared/` (azurerm
   backend from #4): resource group, Container Apps environment,
   `azurerm_servicebus_namespace` with `sku = "Standard"` (no topics yet —
   each service phase below adds its own) — Standard is mandatory per Tech
   Stack: Basic supports neither topics/subscriptions nor the sessions this
   design's ordering guarantee depends on — one shared user-assigned managed
   identity for ACR pulls, and one Azure Container Registry (ACR) shared by
   all 4 services (one registry, one set of push/pull credentials — each
   service gets its own repository *within* that registry, e.g.
   `order-service`, `inventory-service`; grant this shared identity `AcrPull`
   on the registry). Each service's own Terraform (tasks 10/14/17/19) then
   attaches this same identity to its Container App and references it in
   that Container App's `registry` block as the pull credential — ACR
   authentication is configured per-Container-App, not at the Container Apps
   environment level, so the environment resource itself grants nothing on
   its own. Every per-service Terraform module below references this shared
   foundation via remote state / data sources.

   **Data-plane auth (Service Bus + SQL) is passwordless, no Key Vault
   needed.** Each service authenticates to Service Bus and to its own SQL
   database via its own user-assigned managed identity (one identity per
   service, created in that service's own Terraform in tasks 10/14/17/19 —
   distinct from the shared ACR-pull identity above, since Service Bus RBAC
   and SQL AAD-user grants are naturally scoped per-service, not shared):
   - *Service Bus:* each service's identity is granted `Azure Service Bus
     Data Owner` scoped to the namespace (simplest correct RBAC for MVP;
     narrower per-topic/per-subscription roles are unnecessary complexity
     here) — wired in that service's own Terraform task once its
     Container App resource exists, since the role assignment's principal
     is that Container App's identity.
   - *SQL:* each service's own Azure SQL logical server (provisioned in
     tasks 10/14/17) is configured Azure-AD-only auth (no SQL-auth
     admin/password at all), with an `azuread_administrator` block naming
     an AAD **group** (e.g. `sql-admins-order-system`, created in task 5)
     as the server's AAD admin — Azure SQL's `azuread_administrator` only
     accepts a user or group, not a bare service principal/app
     registration, so the CI OIDC app registration (task 6) is added as a
     *member* of this group rather than named directly (a supported
     operation for a service-principal member — Azure SQL resolves group
     admin membership via a live Graph lookup on the token's `oid` at
     connection time, not a token claim, so this works the same as it
     would for a user member); a bare-SP admin block would fail to apply
     or fail to authenticate.

     **Ordering note:** this group-membership resource needs the CI app
     registration's object id, which task 6 creates — task 6's app
     registration + federated-credential creation (the one-time manual
     bootstrap portion, same category as task 4's manual Azure setup) must
     therefore happen *before* task 5's first `terraform apply`, even
     though task 6 is numbered after task 5. Do this bootstrap step
     alongside task 4's manual preconditions, not literally in task-number
     order.

     That service's managed identity is added as a contained database user
     via `CREATE USER ... FROM EXTERNAL PROVIDER` T-SQL, with
     `db_datareader`, `db_datawriter`, **and `db_ddladmin`** roles — the
     first two alone only grant DML (SELECT/INSERT/UPDATE/DELETE); EF Core
     migrations issue DDL (`CREATE TABLE`, `CREATE INDEX`, the
     `__EFMigrationsHistory` table itself), which requires `db_ddladmin` as
     well, or the very first migration against a freshly-provisioned schema-
     less DB fails on a permissions error — **run from inside the same
     `azurerm_container_app_job` that runs EF Core migrations (task 10), not
     via Terraform `null_resource`/`local-exec`**, since `local-exec` runs
     on the
     GitHub-hosted runner, which the SQL network path below deliberately
     does not allow to reach the server; a Terraform-level `local-exec`
     here would hit the identical firewall block and silently never create
     the user.

     Concretely, and this is the part that actually has to carry a real
     credential across the CI-to-Azure boundary: the CI OIDC identity is
     federated (no client secret, no attachable managed identity), so
     `DefaultAzureCredential` running *inside* the container app job cannot
     resolve to it — the job has no way to "be" the CI identity just
     because that identity is the AAD group admin. Instead, the GitHub
     Actions workflow step (already authenticated via `azure/login` per
     task 6's OIDC credential) runs `az account get-access-token
     --resource https://database.windows.net` *before* starting the job,
     and passes the resulting token into `az containerapp job start` as a
     one-shot secret/env var. The job's startup script uses that raw token
     for the `CREATE USER` connection specifically (e.g. `sqlcmd
     --authentication-method=ActiveDirectoryAccessToken -P <token>`, or
     ADO.NET's `SqlConnection.AccessToken` property) — this is the only
     step in the whole plan that uses a short-lived token instead of a
     resolved managed identity, because it's the one step that must
     authenticate as the CI principal rather than the service's own
     identity. Only *after* the contained user exists does the job run
     `dotnet ef database update` using the service's own managed identity
     via `DefaultAzureCredential` (which does resolve correctly, since that
     identity is attached to the job resource) — the same identity the
     running Container App uses afterward.
   - This eliminates the need for Key Vault or any stored connection
     string/secret anywhere in the system — the spec's Tech Stack mentions
     Key Vault as the available secrets mechanism *if* one is needed, but
     since every credential here is a resolved managed-identity token, none
     is; Key Vault is therefore dropped from this plan rather than
     provisioned unused. (If a real secret is ever needed post-MVP — e.g. a
     third-party payment gateway API key once Payment Service stops
     simulating — add Key Vault + a secret reference on that service's
     Container App at that point, not before.)

   **SQL network path:** each service's SQL server (tasks 10/14/17) enables
   "Allow Azure services and resources to access this server"
   (`azurerm_mssql_firewall_rule` with start/end IP `0.0.0.0`) — sufficient
   because both the running Container App and the CI migration step (see
   tasks 10/14/17, revised below) reach SQL from inside Azure, never from
   the public GitHub-hosted runner directly.
   - *Verify:* `terraform validate` + `terraform plan` clean; Service Bus
     namespace exists at `Standard` SKU; ACR exists and the shared
     user-assigned identity has `AcrPull` on it; no Key Vault resource is
     provisioned and no connection-string secret exists anywhere in
     Terraform state or GitHub secrets for SQL or Service Bus.
6. **CI/CD skeleton** — first, create the Azure AD app registration with
   *two* federated OIDC credentials (no long-lived secret): one with
   subject `repo:{org}/{repo}:pull_request` (covers every task's
   PR-triggered build/test/`terraform plan` run, regardless of which
   branch the PR is from) and one with subject
   `repo:{org}/{repo}:ref:refs/heads/main` (covers post-merge `terraform
   apply`/deploy runs on `main`). A single branch-scoped credential would
   only authenticate one branch's runs and break CI on every other task's
   PR — this plan's delivery strategy runs 21 task branches through PR
   CI, so both subjects are required, not optional. Grant the app both
   `Contributor` *and* `User Access Administrator` at the *subscription*
   scope, not resource-group scope — task 5's Terraform is what creates the
   resource group, so a role assignment scoped to that group can't exist
   before task 5's first `apply` runs, and that first `apply` is itself a CI
   job using this same identity. Subscription-scope avoids the
   chicken-and-egg: the identity can create the resource group itself in
   task 5's first CI run. `Contributor` alone is not sufficient: its
   `NotActions` explicitly exclude `Microsoft.Authorization/*/Write`, so it
   cannot create the `azurerm_role_assignment` resources this plan depends
   on throughout — the shared ACR-pull grant (task 5) and each service's
   `Azure Service Bus Data Owner` grant (tasks 10/14/17/19) would otherwise
   fail on `terraform apply` with an authorization error; `User Access
   Administrator` closes that gap. Store its
   client/tenant/subscription IDs as GitHub repo secrets/vars — every job
   below depends on this identity existing, so it's a prerequisite step of
   this task, not a separate one. Then: a reusable *callable* workflow
   (`workflow_call`) per service action (`azure/login` using that OIDC
   credential, build + test, `docker build`/`docker push` to the shared ACR,
   `terraform plan`/`apply`), plus the `terraform plan`/`apply` job for
   `shared/` itself. Rather than one independently-triggered top-level
   workflow file per service — which cannot express a `needs:` dependency
   on another service's job, since GitHub Actions' `needs:` only orders jobs
   *within the same workflow run*, never across separate workflow files —
   there is a single top-level orchestrating workflow
   (`.github/workflows/ci.yml`) triggered on every PR and every push to
   `main`. It always runs a build/test job for `shared/` itself (both new
   test projects from task 1), and detects which `services/<name>/**` paths
   changed (e.g. via `dorny/paths-filter`) to decide which *service* jobs to
   run — but a change under `shared/**` (or the root `Directory.Build.props`/
   `.editorconfig`) is treated as affecting *every* service, not none: it
   forces all 4 service build/test/deploy jobs to run regardless of their
   own path filter, since every service embeds `shared/` by relative-path
   reference and a shared-only PR must still rebuild, retest, and redeploy
   everything that references it (otherwise a shared-only change could merge
   with a green but entirely-skipped check and never reach any running
   container). A PR touching only one service's folder still only runs that
   service's job, keeping the fast path from before. Every service's
   relevant job still lives in the one workflow run for that commit/PR, so
   cross-service `needs:` is always available. When a single PR touches two
   service Terraform folders at once (a later phase's task adding both a
   new topic-owning service and Order Service's subscription to it — see
   tasks 14/17/19), the subscribing service's `terraform apply` job declares
   `needs:` on the topic-owning service's `terraform apply` job — both jobs
   now genuinely exist in the same workflow run, so this ordering is
   actually enforceable, unlike a per-service-file structure.
   - *Verify:* a PR-triggered run (build/test/`terraform plan`) and a
     post-merge run on `main` (`terraform apply`/deploy) both authenticate
     successfully via their respective OIDC credential (no stored
     long-lived Azure credentials anywhere in the repo/secrets); a PR
     touching two service folders at once produces one workflow run with
     both services' jobs and the `needs:` ordering between them visibly
     enforced in the run's job graph.

### Phase 1 — Order Service (core; nothing else can be tested end-to-end without it)
7. **Order domain + persistence** — `Orders`/`OrderItems`/`OrderEvents`
   tables (EF Core, Azure SQL), order state machine enforcing only the
   transitions in the spec's state table, append-only `OrderEvents` write
   on every transition. `DbContext` is configured with
   `EnableRetryOnFailure()` — Azure SQL Serverless auto-pauses when idle
   (used across all three service DBs to keep MVP cost down), and the
   first connection after a resume can take several seconds and needs to
   survive a transient connection failure rather than crash the consumer.
   - *Verify:* unit tests cover every legal transition and reject illegal
     ones (e.g. `CREATED → CONFIRMED` directly); a call against a
     freshly-resumed (simulated transient-failure) connection succeeds via
     the retry policy rather than throwing.
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
   Service Bus redelivers it (after task 3's scheduled delay) once the
   precondition has had time to land. Rely on the subscription's
   `MaxDeliveryCount = 10` (task 3) to dead-letter a message that never
   becomes processable (genuine poison message), rather than building
   custom retry/backoff logic.
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
    a dedicated user-assigned managed identity for this service (separate
    from the shared ACR-pull identity in task 5), granted `Azure Service Bus
    Data Owner` on the shared namespace (data-plane auth — task 3/5);
    Azure SQL Serverless DB with Azure-AD-only auth, the
    `azurerm_mssql_firewall_rule` allowing Azure services (task 5), and this
    service's identity added as a contained DB user via the
    `local-exec`/T-SQL step described in task 5; Container App (image pulled
    from the shared ACR's `order-service` repository; both managed
    identities attached — the shared one for the `registry` block's ACR
    pull credential, this service's own for its runtime env/`DefaultAzureCredential`
    resolution; `min_replicas = 1` — Order Service is also a Service Bus
    consumer of `InventoryReserved`/`InventoryFailed`/`PaymentCompleted`/
    `PaymentFailed`/`InventoryReleased`/`OrderShipped`/`OrderDelivered`
    (task 9), and Container Apps' default scaling only reacts to HTTP
    traffic; between HTTP calls (the common case — a customer places an
    order, then the async pipeline runs with no further HTTP activity) it
    would scale to zero and never wake to consume those events, leaving the
    order stuck in `CREATED` forever. Same reasoning as Inventory/Payment/
    Fulfillment's `min_replicas = 1` in tasks 14/17/19); and the topics Order
    *owns* (`OrderCreated`, `OrderCancelled`, `OrderConfirmed`).

    **Migrations run inside Azure, not from the GitHub-hosted runner** — the
    runner has no network path to the SQL server (firewall only allows
    Azure services, task 5) and no way to acquire the service's managed-
    identity token for AAD SQL auth. Instead, this service's Terraform also
    defines a `azurerm_container_app_job` (manual-trigger, same image, same
    Container Apps environment) whose command first runs the `CREATE USER
    ... FROM EXTERNAL PROVIDER` T-SQL for this service's own identity
    (idempotent, using the short-lived AAD access token the CI workflow
    step fetches and passes in — task 5's SQL bullet has the full
    credential hand-off), then runs `dotnet ef database update`
    (authenticating as this service's own managed identity, now a
    contained user, via `DefaultAzureCredential`); CI/CD builds the image,
    pushes to ACR tagged with the commit SHA, then invokes `az containerapp
    job start` against that job (wrapped in a short retry/wait loop — a
    one-shot run
    isn't covered by the application-level `EnableRetryOnFailure()` from
    task 7, and can hit a freshly-idle Serverless DB's resume window) and
    polls for the job execution to succeed before deploying the new
    Container App revision. Every subsequent service's migration step
    (tasks 14, 17) follows this same in-Azure job pattern.
    - *Verify:* `docker build -f services/order-service/Dockerfile .` (run
      from repo root) succeeds locally; `terraform plan` clean inside
      `services/order-service/infra/terraform/`; the service's managed
      identity shows as a contained user in the SQL DB with `Azure Service
      Bus Data Owner` on the namespace; CI workflow builds, pushes, runs the
      migration Container Apps Job successfully against a freshly-provisioned
      (schema-less) DB, and deploys.

**Checkpoint:** Order Service is deployed and fully testable (API +
consumers) standalone, with hand-published inventory/payment events (via
the in-memory bus in tests) standing in for the other services. Confirm
before starting Phase 2.

### Phase 2 — Inventory Service
11. **Inventory domain + persistence** — `InventoryItems` table, the
    `InventoryReservations` table (per-order/per-product record of what was
    reserved, deleted on release — the source of truth for exactly what to
    restore), and the `InventoryOrderOutcomes` table (permanent, one row per
    order, never deleted — the actual idempotency guard; see Data Model for
    why `InventoryReservations` alone can't serve that role once a
    reservation is released). `DbContext` uses `EnableRetryOnFailure()`
    (same reason as task 7 — Serverless auto-pause).
12. **Reserve on `OrderCreated`** — first, check `InventoryOrderOutcomes` for
    an existing row for this `orderId`; if found, no-op/re-publish that
    row's recorded outcome (`InventoryReserved` or `InventoryFailed`) instead
    of processing again — this is the only idempotency check, and it must
    run before touching `InventoryItems`/`InventoryReservations`, because a
    redelivered `OrderCreated` can arrive *after* the order was already
    reserved-then-released-then-cancelled, by which point
    `InventoryReservations` has no row left to detect that. If no outcome
    row exists, proceed: for each item, an atomic conditional update (`UPDATE
    InventoryItems SET Available = Available - @qty WHERE ProductId = @p AND
    Available >= @qty`, single DB transaction) so two concurrent orders
    can't both win the last unit. On success across every line, write one
    `InventoryReservations` row per item, write an `InventoryOrderOutcomes`
    row `{ orderId, Outcome: Reserved }`, and publish `InventoryReserved {
    orderId, totalAmount, paymentMethod }` — the latter two fields are
    carried through unchanged from `OrderCreated`'s payload, since Inventory
    Service has no use for them itself and Payment Service (which consumes
    `InventoryReserved` next) has no other way to learn what to charge; on
    any line failing, roll back the `InventoryItems`/`InventoryReservations`
    changes, write an `InventoryOrderOutcomes` row `{ orderId, Outcome:
    OutOfStock }`, and publish `InventoryFailed { reason: OutOfStock }`.
    - *Verify:* unit test — order with one out-of-stock item reserves
      nothing, writes an `OutOfStock` outcome row, and publishes
      `InventoryFailed`; concurrent orders for the same last unit never both
      succeed (NFR: no overselling); re-delivering the same `OrderCreated`
      after a successful reservation does not decrement `Available` a
      second time; re-delivering the same `OrderCreated` *after* its
      reservation has since been released (`OrderCancelled` already
      processed, `InventoryReservations` rows already deleted) is still a
      no-op — it must not re-decrement `Available` or write a new
      reservation, because the `InventoryOrderOutcomes` row is still there.
13. **Release on `OrderCancelled`** — read the order's `InventoryReservations`
    rows to get the exact product/quantity pairs to restore, add them back
    to `InventoryItems.Available`, delete the reservation rows, publish
    `InventoryReleased`; no-ops if no reservation rows exist for that order
    (idempotent, and safe against `OrderCancelled` arriving for an order
    that was never reserved, i.e. the `InventoryFailed` path). The
    `InventoryOrderOutcomes` row is *not* deleted here — it must survive
    release permanently so a much-later redelivery of the same
    `OrderCreated` is still recognized as already-processed (task 12).
14. **Inventory Service Dockerfile, Terraform + deploy** —
    `services/inventory-service/Dockerfile` (same repo-root-build-context
    pattern as Order Service's); `services/inventory-service/infra/terraform/`:
    same per-service identity / Service Bus RBAC / SQL AAD-auth + firewall
    pattern as task 10 (own managed identity granted `Azure Service Bus Data
    Owner` on the namespace and added as a contained user on its own SQL
    DB); Container App (image from the shared ACR's `inventory-service`
    repository, both the shared ACR-pull identity and this service's own
    identity attached; `min_replicas = 1` — Inventory has no HTTP traffic to
    trigger the default scale-from-zero, so without a nonzero floor a
    consumer with 0 replicas never picks up messages published to its
    subscriptions), Azure SQL Serverless DB, Inventory's subscriptions to
    Order's `OrderCreated` and `OrderCancelled` topics (which now exist as
    of Phase 1), and the topics Inventory *owns* (`InventoryReserved`,
    `InventoryFailed`, `InventoryReleased`); also adds Order Service's
    subscriptions to these three new topics (Order's consumer code from
    Phase 1 task 9 has been waiting for them) as a small addition to
    `services/order-service/infra/terraform/`. CI/CD job builds/pushes the
    image and deploys Inventory Service; per task 6, Order Service's
    `terraform apply` job in this PR's workflow run declares `needs:` on
    Inventory Service's `terraform apply` job, so Order's subscriptions
    aren't applied before Inventory's topics exist. CI/CD job also runs the
    migration Container Apps Job against Inventory's DB before its Container
    App revision goes live (same in-Azure pattern as task 10, not a direct
    connection from the GitHub-hosted runner).
    - *Verify:* `docker build -f services/inventory-service/Dockerfile .`
      succeeds locally; `terraform plan` clean for both service folders'
      Terraform; CI builds, pushes, migrates the DB schema, and deploys; a
      manually-published `OrderCreated` reaches deployed Inventory and a
      resulting `InventoryReserved`/`InventoryFailed` reaches deployed Order
      Service.

### Phase 3 — Payment Service
15. **Payment domain + persistence** — `Payments` table (`OrderId` stored as
    a plain column, not a DB-level FK — `Orders` lives in a different
    service's database; see Data Model). `DbContext` uses
    `EnableRetryOnFailure()` (same reason as task 7 — Serverless auto-pause).
16. **Charge on `InventoryReserved`** — simulated payment gateway call for
    `totalAmount` against `paymentMethod` (both read directly from the
    consumed event's payload, not queried from any other service's DB),
    keyed by `orderId` as idempotency key; publish `PaymentCompleted` or
    `PaymentFailed`.
    - *Verify:* unit test — re-delivering `InventoryReserved` for an
      already-charged order does not double-charge (idempotency NFR +
      "no uncharged confirmation" NFR).
17. **Payment Service Dockerfile, Terraform + deploy** —
    `services/payment-service/Dockerfile` (same pattern);
    `services/payment-service/infra/terraform/`: same per-service identity /
    Service Bus RBAC / SQL AAD-auth + firewall pattern as task 10; Container
    App (image from the shared ACR's `payment-service` repository, both the
    shared ACR-pull identity and this service's own identity attached;
    `min_replicas = 1`, same reason as Inventory's — no HTTP traffic to
    scale from zero on), Azure SQL Serverless DB, Payment's subscription to
    Inventory's `InventoryReserved` topic, and the topics Payment *owns*
    (`PaymentCompleted`, `PaymentFailed`); also adds Order Service's
    subscriptions to these two new topics (in
    `services/order-service/infra/terraform/`). CI/CD job builds/pushes the
    image and deploys Payment Service; per task 6, Order Service's
    `terraform apply` job in this PR's workflow run declares `needs:` on
    Payment Service's `terraform apply` job. CI/CD job also runs the
    migration Container Apps Job against Payment's DB before its Container
    App revision goes live (same in-Azure pattern as task 10).
    - *Verify:* `docker build -f services/payment-service/Dockerfile .`
      succeeds locally; `terraform plan` clean; CI builds, pushes, migrates
      the DB schema, and deploys; end-to-end through Payment reaches
      `CONFIRMED` or `CANCELLED` correctly on deployed infra.

### Phase 4 — Fulfillment Service
18. **Simulated shipping on `OrderConfirmed`** — publish `OrderShipped`
    then `OrderDelivered` (simulated delay, no carrier integration).
19. **Fulfillment Service Dockerfile, Terraform + deploy** —
    `services/fulfillment-service/Dockerfile` (same pattern);
    `services/fulfillment-service/infra/terraform/`: own managed identity
    granted `Azure Service Bus Data Owner` on the namespace (no SQL AAD-user
    step needed — no DB); Container App (image from the shared ACR's
    `fulfillment-service` repository, both the shared ACR-pull identity and
    this service's own identity attached; `min_replicas =
    1`, same reason as Inventory/Payment; no DB — Fulfillment owns no
    tables per the spec's Data Model), Fulfillment's
    subscription to Order's `OrderConfirmed` topic (created in Phase 1,
    finally subscribed to here), and the topics Fulfillment *owns*
    (`OrderShipped`, `OrderDelivered`); also adds Order Service's
    subscriptions to these two new topics (in
    `services/order-service/infra/terraform/`). CI/CD job builds/pushes the
    image and deploys Fulfillment Service; per task 6, Order Service's
    `terraform apply` job in this PR's workflow run declares `needs:` on
    Fulfillment Service's `terraform apply` job.
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
