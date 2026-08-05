# CI/CD workflows

`ci.yml` is the top-level orchestrating workflow, triggered on every PR and
every push to `main`. Everything else here except `auto-fix-ci.yml`,
`pr-review.yml`, and `pr-description.yml` is a reusable (`workflow_call`)
template invoked as a job within it, so cross-job `needs:` ordering (e.g. a
later phase's service depending on another service's `terraform apply`) is
always available within one workflow run.

`auto-fix-ci.yml` is another independently-triggered workflow: a
`workflow_run` watcher on `ci.yml` that, on failure, hands the run to the
`ci-fixer` subagent ([.claude/agents/ci-fixer.md](../../.claude/agents/ci-fixer.md))
to investigate and fix. A PR-triggered failure gets its fix pushed to the
same branch; a post-merge failure on `main` (which can mean a live
Terraform apply or DB migration went wrong) never gets a direct push —
the agent opens a new PR instead, so it's reviewed before retrying.

`pr-review.yml` is the third independently-triggered workflow: on every
PR opened/synchronized/reopened, it hands the diff to the `pr-reviewer`
subagent ([.claude/agents/pr-reviewer.md](../../.claude/agents/pr-reviewer.md)),
adapted from addy-osmani/agent-skills' `code-review-and-quality` skill.
Critical and Important findings are fixed and pushed directly to the PR
branch as an `[auto-review]`-prefixed commit (that prefix is also the
loop-guard that stops the workflow reviewing its own commit); Suggestions
are left as comments only. `infra/terraform/**` and
`services/*/src/*/DbMigration/**` are excluded from auto-fix even for
Critical/Important findings — those get reported in the PR comment
instead, matching this repo's existing rule (`CLAUDE.md`, `ci-fixer.md`)
that Terraform and DB-schema changes need human review, not an autonomous
push. Fork PRs are skipped, same as `auto-fix-ci.yml`.

`pr-description.yml` is the fourth independently-triggered workflow: on
every PR opened/synchronized, it hands the diff to the `pr-describer`
subagent ([.claude/agents/pr-describer.md](../../.claude/agents/pr-describer.md)),
which writes the PR body as three sections — Summary, Details (with a
collapsible per-file breakdown), and an unticked Test plan checklist — via
`gh pr edit`. It regenerates the body in full on every push rather than
diffing against the previous one, so any manual edits (e.g. ticked
checkboxes) made between pushes get overwritten by the next one. Fork PRs
are skipped, same as the other agent-driven workflows.

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
are added to `ci.yml` once that service actually has its own `Dockerfile`
and `infra/terraform/` — invoking either reusable workflow against a
service that doesn't have those files yet would just fail.

Order Service (and every other service with its own DB) needs
migrations applied inside Azure between building the image and rolling out
the new Container App revision, so its post-merge deploy isn't a plain
`_terraform-plan-apply.yml` call: `order-service-deploy` in `ci.yml` runs
`terraform apply -target=azurerm_container_app_job.order_service_migrate`
first (updates only the migration job's image), triggers that job via `az
containerapp job start` with a freshly-fetched SQL AAD access token, polls
for its completion, then runs a second, untargeted `terraform apply` that
updates the Container App itself. PR runs still get a plain `plan` via the
reusable workflow, since a plan doesn't deploy anything.
