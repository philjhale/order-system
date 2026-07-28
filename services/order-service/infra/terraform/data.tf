# Reads the shared foundation's state (infra/terraform/shared) — resource group, Container Apps
# environment, Service Bus namespace, ACR, and the SQL AAD-admin group — rather than duplicating
# any of it here.
data "terraform_remote_state" "shared" {
  backend = "azurerm"

  config = {
    resource_group_name  = "rg-order-system-bootstrap"
    storage_account_name = "stordersystemtfstate01"
    container_name       = "tfstate"
    key                  = "shared.tfstate"
  }
}
