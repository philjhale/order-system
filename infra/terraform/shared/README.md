# Shared foundation

`terraform apply` here creates the resources every service depends on:

- Resource group `rg-order-system`
- Container Apps environment `cae-order-system` (all 4 services' Container
  Apps run inside this one environment)
- Service Bus namespace `sb-order-system-01` at `Standard` SKU (mandatory —
  Basic has no topics/subscriptions or sessions). No topics/subscriptions
  yet; each service phase creates its own in its own Terraform.
- User-assigned managed identity `id-order-system-acr-pull`, granted
  `AcrPull` on the shared registry. Each service's own Terraform
  (tasks 10/14/17/19) attaches this same identity to its Container App.
- Container Registry `acrordersystem01`, shared by all 4 services (each
  gets its own repository within it, e.g. `order-service`).
- AAD group `sql-admins-order-system`, with the CI app registration
  (`infra/terraform-bootstrap/README.md` section 4) added as a member.
  Each service's own SQL server (tasks 10/14/17) names this group as its
  `azuread_administrator` — Azure SQL only accepts a user or group there,
  not a bare service principal.

No Key Vault, no stored connection strings/secrets: Service Bus and SQL
data-plane auth are both passwordless via managed identity, wired in each
service's own Terraform.

## Apply

Requires `infra/terraform-bootstrap` to have been applied first (this
config's backend points at the storage account it creates), and the CI
app registration from that bootstrap's section 4 to already exist (this
config looks up its service principal by client ID).

```bash
cd infra/terraform/shared
terraform init
terraform plan
terraform apply
```

In CI, this runs as the `terraform plan`/`apply` job for `shared/`
described in task 6, authenticated via the same CI app registration via
OIDC (`azure/login`).

## Consumed by

Every per-service Terraform config (`services/<name>/infra/terraform/`)
reads this config's outputs via a `terraform_remote_state` data source
pointed at the same backend (key `shared.tfstate`), rather than
duplicating any of the above.
