# CI/CD workflows

`ci.yml` is the single top-level orchestrating workflow, triggered on every
PR and every push to `main`. It's the only independently-triggered
workflow file — everything else here is a reusable (`workflow_call`)
template invoked as a job within it, so cross-job `needs:` ordering (e.g.
a later phase's service depending on another service's `terraform apply`)
is always available within one workflow run.

- `_dotnet-build-test.yml` — `dotnet restore`/`build`/`test` against a
  given `.sln`.
- `_docker-build-push.yml` — builds a service's image and pushes it to the
  shared ACR (`acrordersystem01`), tagged `:latest` and `:${{ github.sha }}`.
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
