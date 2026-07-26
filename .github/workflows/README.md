# CI/CD workflows

`ci.yml` is the single top-level orchestrating workflow, triggered on every
PR and every push to `main`. It's the only independently-triggered
workflow file — everything else here is a reusable (`workflow_call`)
template invoked as a job within it, so cross-job `needs:` ordering (e.g.
a later phase's service depending on another service's `terraform apply`)
is always available within one workflow run.

- `_dotnet-build-test.yml` — `dotnet restore`/`build`/`test` against a
  given `.sln`.
- `_docker-build-push.yml` — builds a service's image, tagged `:latest`
  and `:${{ github.sha }}`. Its `push: false`/`true` input gates whether
  it also pushes to the shared ACR (`acrordersystem01`) — callers should
  pass `push: ${{ github.event_name == 'push' }}` so a PR run only
  validates the Dockerfile builds, never pushing an unmerged image or
  overwriting the mutable `:latest` tag.
- `_terraform-plan-apply.yml` — `terraform plan` on PR runs, `terraform
  apply` on `main` runs, against a given working directory.

All three authenticate to Azure via `azure/login` using the
`order-system-ci` app registration's OIDC federated credentials (no
stored client secret) — `pull_request` subject for PR runs, `main` branch
subject for post-merge runs (`infra/terraform-bootstrap/README.md` section
4). Client/tenant/subscription IDs are repo variables (`AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`), not secrets — they're
non-sensitive identifiers, and OIDC means there's no long-lived credential
to protect alongside them.

`ci.yml` always runs `shared/`'s build/test job and the `terraform/shared`
plan/apply job. It detects which `services/<name>/**` folders a PR/push
touches (`dorny/paths-filter`) to decide which service build/test jobs to
run — except a `shared/**` or root-config change, which is treated as
affecting *every* service, since each one references `shared/` by
relative-path project reference.

Each service's `docker-build-push` and `terraform-plan-apply` job calls
are added to `ci.yml` by that service's own deploy task (10/14/17/19),
once its `Dockerfile` and `infra/terraform/` actually exist — invoking
either reusable workflow against a service that doesn't have those files
yet would just fail.

Order Service (task 10, and every future service with its own DB) needs
migrations applied inside Azure between building the image and rolling out
the new Container App revision, so its post-merge deploy isn't a plain
`_terraform-plan-apply.yml` call: `order-service-deploy` in `ci.yml` runs
`terraform apply -target=azurerm_container_app_job.order_service_migrate`
first (updates only the migration job's image), triggers that job via `az
containerapp job start` with a freshly-fetched SQL AAD access token, polls
for its completion, then runs a second, untargeted `terraform apply` that
updates the Container App itself. PR runs still get a plain `plan` via the
reusable workflow, since a plan doesn't deploy anything.
