# Order System MVP

## Why

Demo distributed order system: Order, Inventory, Payment, and Fulfillment
services react to each other's events with no central orchestrator, while
keeping inventory and payment consistent under at-least-once, out-of-order
event delivery. Full requirements/scope: [docs/SPEC.md](docs/SPEC.md).

Don't add functionality the spec explicitly defers (Cart, Notifications,
Search, API Gateway, sagas, auth, refunds, cancellation) — flag it instead
of building it.

## What

- .NET 10 / C#. Each service (`services/<name>/`) has its own `.sln` and
  the same internal layout: `src/Api`, `Domain`, `Persistence`,
  `Messaging`, `Consumers`, `HealthChecks`, `DbMigration`, with a sibling
  `tests/<Service>.Tests` mirroring `src/`. Put new code in the matching
  folder.
- Services talk to each other only via Azure Service Bus events —
  no direct references across `services/*/src`, `tests`, or
  `infra/terraform`. Shared code lives in `shared/`.
- Each service backed by Azure SQL where needed; infra is Terraform,
  deployed as Azure Container Apps. No local docker-compose stack. Terraform
  conventions (apply order, Service Bus RBAC scoping): see
  [.claude/rules/terraform.md](.claude/rules/terraform.md).

## How

```bash
# shared libraries
dotnet build shared/OrderSystem.Shared.sln
dotnet test shared/OrderSystem.Shared.sln

# a service (same pattern for inventory-service, payment-service, fulfillment-service)
dotnet build services/order-service/OrderSystem.OrderService.sln
dotnet test services/order-service/OrderSystem.OrderService.sln

# cross-service behaviour
dotnet test integration-tests/OrderSystem.IntegrationTests.sln
```

CI (`.github/workflows/ci.yml`) builds/tests only the services a PR
actually touches, plans Terraform on PRs, and applies + deploys on merge
to `main`.

Building a new feature/task end-to-end: see
[agent_docs/build-workflow.md](agent_docs/build-workflow.md).

## Boundaries

- Never commit secrets, connection strings, or `*.tfvars`/`.env` files —
  Azure SQL and Service Bus credentials come from Terraform outputs and
  Container App secrets, not checked-in config.
- Ask before running `terraform apply` locally — infra changes go through
  the PR → plan → merge → CI-apply flow, not ad hoc applies.
- Ask before modifying a service's DB schema or adding an EF Core
  migration (`DbMigration/`) — it affects a live Azure SQL database.
