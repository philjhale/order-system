# Order System

A demo distributed order system. The purpose is to be a vehicle for practicing AI driven development using [agent-skills](https://github.com/addyosmani/agent-skills) not to be a realistic, perfect example of a distributed system.

Services (Order, Inventory, Payment, Fulfillment) react to each other's
events with no central orchestrator, while keeping
inventory and payment state consistent under at-least-once, out-of-order
event delivery. Full requirements, scope, data model, and event flow: [docs/SPEC.md](docs/SPEC.md).

## Setup

There's no local docker-compose stack — each service runs as an Azure
Container App, provisioned by Terraform, backed by Service Bus and (where
needed) Azure SQL. Infra is applied in order:

1. [infra/terraform-bootstrap](infra/terraform-bootstrap/README.md) — one-time,
   manual. Creates the Terraform remote-state backend and the CI app
   registration/OIDC federated credentials.
2. [infra/terraform/shared](infra/terraform/shared/README.md) — shared
   resource group, Container Apps environment, Service Bus namespace,
   container registry, and SQL AAD-admin group. Requires step 1.
3. Each `services/<name>/infra/terraform/` — per-service resources (Container
   App, SQL server/DB where applicable), reading step 2's outputs via remote
   state.

## Running / building locally

Each service and the shared libraries have their own `.sln`; build and test
with the standard .NET CLI, e.g.:

```bash
dotnet build shared/OrderSystem.Shared.sln
dotnet test shared/OrderSystem.Shared.sln

dotnet build services/order-service/OrderSystem.OrderService.sln
dotnet test services/order-service/OrderSystem.OrderService.sln
```

Cross-service behaviour is covered by
[integration-tests/OrderSystem.IntegrationTests.sln](integration-tests/OrderSystem.IntegrationTests.sln).

## CI/CD

PRs and pushes to `main` run through a single GitHub Actions workflow,
[.github/workflows/ci.yml](.github/workflows/ci.yml), which builds/tests each
changed service, `terraform plan`s on PRs, and `terraform apply`s +
deploys on merge to `main`. A second, independently-triggered workflow,
[.github/workflows/auto-fix-ci.yml](.github/workflows/auto-fix-ci.yml),
watches `ci.yml` and investigates/fixes failures automatically. See
[.github/workflows/README.md](.github/workflows/README.md) for how all of
these fit together.

### Repository secrets

Azure access uses OIDC (no stored credential) and repo *variables*
(`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` — see
[.github/workflows/README.md](.github/workflows/README.md)), so those
aren't secrets. The one actual repository secret this project needs:

- **`CLAUDE_CODE_OAUTH_TOKEN`** — used by `auto-fix-ci.yml` to run
  `anthropics/claude-code-action` under a Claude subscription (Pro, Max,
  Team, or Enterprise) rather than a pay-per-token API key. Generate it
  locally, then add it under repo Settings → Secrets and variables →
  Actions:

  ```bash
  claude setup-token
  ```

  This opens a browser login against your Claude subscription and prints
  a long-lived OAuth token. It can expire or be invalidated if you log
  out of Claude elsewhere, in which case re-run the command and update
  the secret.

  `auto-fix-ci.yml` also requires the
  [Claude GitHub App](https://github.com/apps/claude) to be installed on
  this repository (Contents, Issues, and Pull requests: read & write).
