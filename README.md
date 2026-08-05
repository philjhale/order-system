# Order System

A demo distributed order system. The purpose is to be a vehicle for practicing AI driven development using [agent-skills](https://github.com/addyosmani/agent-skills) not to be a realistic, perfect example of a distributed system.

Services (Order, Inventory, Payment, Fulfillment) react to each other's
events with no central orchestrator, while keeping
inventory and payment state consistent under at-least-once, out-of-order
event delivery. Full requirements, scope, data model, and event flow: [docs/SPEC.md](docs/SPEC.md).

## Setup

There's no local docker-compose stack — each service runs as an Azure
Container App, provisioned by Terraform, backed by Service Bus and (where
needed) Azure SQL.

### Deploying

Each config depends on the previous one's remote state, so apply in this
order:

1. [infra/terraform-bootstrap](infra/terraform-bootstrap/README.md) — one-time,
   manual. Creates the Terraform remote-state backend and the CI app
   registration/OIDC federated credentials.
2. [infra/terraform/shared](infra/terraform/shared/README.md) — shared
   resource group, Container Apps environment, Service Bus namespace,
   container registry, and SQL AAD-admin group. Requires step 1.
3. Each `services/<name>/infra/terraform/` — per-service resources (Container
   App, SQL server/DB where applicable), reading step 2's outputs via remote
   state.

Each config is applied the same way, e.g. for the shared foundation:

```bash
cd infra/terraform/shared
terraform init
terraform plan
terraform apply
```

Once `shared` is applied, do the same in each `services/<name>/infra/terraform/`
you want running. See each directory's own README for what it creates and
any manual one-time steps (e.g. order-service's Directory Readers role
grant).

In CI, `shared` and each touched service apply automatically on merge to
`main` (see [CI/CD](#cicd) below) — the steps above are for a local/manual
deploy.

### Destroying

Tear down in the reverse order of apply, since each config depends on the
one before it via `terraform_remote_state`:

```bash
# 1. each service you deployed, e.g.:
cd services/order-service/infra/terraform
terraform destroy

# 2. the shared foundation, once no service still depends on it
cd infra/terraform/shared
terraform destroy

# 3. terraform-bootstrap — only if tearing down entirely; this holds the
# remote-state backend every other config's `terraform init` needs, so
# leave it in place if you intend to redeploy later
cd infra/terraform-bootstrap
terraform destroy
```

No resource in this repo's Terraform has `prevent_destroy` or other
destroy protection — this is a demo environment with no durable data, and
the whole point is that it can be spun up and torn down freely with no
lingering cost. `terraform destroy` in each directory should complete
without manual intervention beyond confirming the prompt.

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
deploys on merge to `main`. See
[.github/workflows/README.md](.github/workflows/README.md) for how the
reusable build/test, Docker, and Terraform jobs fit together.
