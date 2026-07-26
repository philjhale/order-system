# Terraform remote-state bootstrap

One-time, manual, local-state Terraform. Creates the Azure Storage Account
+ blob container used as the `azurerm` backend by every other Terraform
config in this repo (`infra/terraform/shared/`, and each
`services/<name>/infra/terraform/`). Not run in CI — those configs need
this backend to already exist before they can use it.

## 1. Select the subscription

```bash
az login
az account set --subscription <subscription-id>
```

## 2. Register required resource providers

A subscription that has never used these services fails `terraform apply`
with `MissingSubscriptionRegistration` until they're registered.
Registration can take several minutes.

```bash
for ns in Microsoft.App Microsoft.ServiceBus Microsoft.Sql Microsoft.ContainerRegistry Microsoft.Storage; do
  az provider register --namespace "$ns"
done

# Poll until every namespace reports "Registered":
az provider list --query "[?namespace=='Microsoft.App' || namespace=='Microsoft.ServiceBus' || namespace=='Microsoft.Sql' || namespace=='Microsoft.ContainerRegistry' || namespace=='Microsoft.Storage'].{ns:namespace, state:registrationState}" -o table
```

## 3. Apply this config

```bash
cd infra/terraform-bootstrap
terraform init
terraform apply
```

Creates:
- Resource group `rg-order-system-bootstrap` (region: `uksouth`)
- Storage account `stordersystemtfstate01` (versioning enabled)
- Blob container `tfstate`

Every other Terraform config's backend block points at this storage
account/container, e.g.:

```hcl
terraform {
  backend "azurerm" {
    resource_group_name = "rg-order-system-bootstrap"
    storage_account_name = "stordersystemtfstate01"
    container_name       = "tfstate"
    key                  = "shared.tfstate" # or "<service>.tfstate"
  }
}
```

## 4. CI identity bootstrap (task 6's app-registration step, done early)

Task 5's Terraform needs the CI app registration's object id (for the SQL
AAD-admin group membership), so this app registration is created here
rather than waiting for task 6's number to come up. One-time, manual,
via `az` — not Terraform, since the app registration and its federated
credentials must exist before any CI-authenticated `terraform apply` can
run.

```bash
az ad app create --display-name "order-system-ci"
az ad sp create --id <appId>

az ad app federated-credential create --id <app-object-id> --parameters '{
  "name": "order-system-pr",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>@<owner-id>/<repo>@<repo-id>:pull_request",
  "audiences": ["api://AzureADTokenExchange"]
}'
az ad app federated-credential create --id <app-object-id> --parameters '{
  "name": "order-system-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>@<owner-id>/<repo>@<repo-id>:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

az role assignment create --assignee-object-id <sp-object-id> --assignee-principal-type ServicePrincipal --role "Contributor" --scope "/subscriptions/<subscription-id>"
az role assignment create --assignee-object-id <sp-object-id> --assignee-principal-type ServicePrincipal --role "User Access Administrator" --scope "/subscriptions/<subscription-id>"

# ARM roles above cover azurerm_* resources but not azuread_* ones (task
# 5's sql_admins group/membership) — the azuread Terraform provider talks
# to Microsoft Graph, which ARM RBAC doesn't reach. Without this, `terraform
# plan`/`apply` against azuread_group/azuread_group_member fails with a
# Graph 403 "Insufficient privileges". Tenant-wide (Graph app permissions
# aren't scopable to one group), but low-stakes in a single-project tenant.
az ad app permission add --id <app-object-id> \
  --api 00000003-0000-0000-c000-000000000000 \
  --api-permissions 62a82d76-70ea-41e2-9197-370581804d09=Role  # Group.ReadWrite.All
az ad app permission admin-consent --id <app-object-id>
```

**Federated credential subject format:** GitHub's OIDC `sub` claim uses
`repo:<owner>@<owner-id>/<repo>@<repo-id>:...` (ID-qualified), not the
plain `repo:<owner>/<repo>:...` form shown in most docs/examples — at
least for this repo (confirmed live in task 6's first CI run, which
failed OIDC token exchange with `AADSTS700213: No matching federated
identity record` until the credentials were updated to match). Look up
the owner/repo numeric IDs with `gh api repos/<owner>/<repo> --jq
'{owner_id: .owner.id, repo_id: .id}'` before creating these credentials,
and don't assume the plain form works without checking a real run.

**Accepted risk:** the `pull_request` federated credential subject
(`repo:philjhale/order-system:pull_request`) trusts *any* PR from this
repo, not just a specific branch — combined with `Contributor` +
`User Access Administrator` at subscription scope on the same principal,
a malicious or compromised PR that modifies `.github/workflows/**` could
use these credentials to escalate IAM roles at subscription scope. This
breadth is required by the plan (every task's PR needs `terraform plan`
to authenticate, per `tasks/plan.md` task 6) and is an accepted tradeoff
for this MVP, not an oversight. Mitigate by enabling branch protection
requiring review on changes to `.github/workflows/**` before merge.

No client secret is created — authentication is OIDC-only via the two
federated credentials above. Task 6 stores the app (client) ID, tenant ID,
and subscription ID as GitHub repo secrets/vars for use by
`azure/login` in CI workflows; task 5's Terraform references this app's
service-principal object id as a member of the SQL AAD-admin group.
