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
  "subject": "repo:philjhale/order-system:pull_request",
  "audiences": ["api://AzureADTokenExchange"]
}'
az ad app federated-credential create --id <app-object-id> --parameters '{
  "name": "order-system-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:philjhale/order-system:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

az role assignment create --assignee-object-id <sp-object-id> --assignee-principal-type ServicePrincipal --role "Contributor" --scope "/subscriptions/<subscription-id>"
az role assignment create --assignee-object-id <sp-object-id> --assignee-principal-type ServicePrincipal --role "User Access Administrator" --scope "/subscriptions/<subscription-id>"
```

No client secret is created — authentication is OIDC-only via the two
federated credentials above. Task 6 stores the app (client) ID, tenant ID,
and subscription ID as GitHub repo secrets/vars for use by
`azure/login` in CI workflows; task 5's Terraform references this app's
service-principal object id as a member of the SQL AAD-admin group.
