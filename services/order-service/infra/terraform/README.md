# Order Service infrastructure

`terraform apply` here creates everything Order Service needs on top of the
shared foundation (`infra/terraform/shared/`):

- User-assigned managed identity `id-order-service`, granted `Azure Service
  Bus Data Owner` on the shared Service Bus namespace.
- SQL server `sql-order-service` (Azure-AD-only auth, `sql-admins-order-system`
  as AAD admin) and database `order-service` (Serverless `GP_S_Gen5_1`,
  auto-pauses after an hour idle), plus a firewall rule allowing Azure
  services. This service's own identity is **not** added as a contained DB
  user by Terraform — the GitHub-hosted runner has no network path to the
  server — that happens inside the migration job below instead.
- Container App `ca-order-service` (image pulled from the shared ACR's
  `order-service` repository; `min_replicas = 1`, since this service also
  consumes Service Bus events and Container Apps' default scaling only
  reacts to HTTP traffic).
- Container App Job `caj-order-service-migrate` (manual trigger): runs the
  same image as `dotnet OrderSystem.OrderService.dll migrate`, which first
  provisions this service's managed identity as a contained DB user (using a
  short-lived AAD token for `https://database.windows.net` that CI fetches
  and passes in via `--env-vars` at trigger time — see `.github/workflows/`)
  and then applies EF Core migrations.
- Service Bus topics owned by this service: `OrderCreated`, `OrderCancelled`,
  `OrderConfirmed`. Other services' subscriptions to these are added by
  their own Terraform once they exist, not here.

No Key Vault, no stored connection strings/secrets: every credential here is
either a resolved managed-identity token or (for the one-time contained-user
creation) a short-lived token CI fetches fresh on every deploy.

## Apply

Requires `infra/terraform/shared` to already be applied (this config reads
its outputs via `terraform_remote_state`).

```bash
cd services/order-service/infra/terraform
terraform init
terraform plan
terraform apply
```

In CI, this runs as this service's `terraform plan`/`apply` job
(`.github/workflows/ci.yml`), authenticated the same way as `shared/`. CI
also passes `-var image_tag=<commit-sha>` on `apply` so the Container App
and migration job point at the image `docker-build-push` just pushed, then
triggers the migration job and polls it for success before the new Container
App revision goes live.
