resource "azurerm_resource_group" "shared" {
  name     = "rg-order-system"
  location = var.location
}

resource "azurerm_container_app_environment" "shared" {
  name                = "cae-order-system"
  location            = azurerm_resource_group.shared.location
  resource_group_name = azurerm_resource_group.shared.name
}

# Standard is mandatory, not a cost choice: Basic supports neither
# topics/subscriptions nor the sessions this design's per-order ordering
# guarantee depends on.
resource "azurerm_servicebus_namespace" "shared" {
  name                = "sb-order-system-01"
  location            = azurerm_resource_group.shared.location
  resource_group_name = azurerm_resource_group.shared.name
  sku                 = "Standard"

  # AAD-only data-plane auth (managed identities, no SAS connection
  # strings) — matches the no-Key-Vault, no-stored-secret design for
  # every other credential in this system.
  local_auth_enabled = false

  # No fixed IP range to allow-list: reached from developer machines and
  # from each service's Container App / GitHub-hosted CI runners, none
  # with a stable IP.
  public_network_access_enabled = true
}

# Shared across all 4 services' Container Apps (attached per-app, not
# environment-wide — ACR auth is configured per-Container-App).
resource "azurerm_user_assigned_identity" "acr_pull" {
  name                = "id-order-system-acr-pull"
  location            = azurerm_resource_group.shared.location
  resource_group_name = azurerm_resource_group.shared.name
}

# One registry shared by all 4 services, each getting its own repository
# within it (e.g. "order-service", "inventory-service").
resource "azurerm_container_registry" "shared" {
  name                = "acrordersystem01"
  location            = azurerm_resource_group.shared.location
  resource_group_name = azurerm_resource_group.shared.name
  sku                 = "Basic"

  # Explicit, not relying on the provider default: pulls authenticate via
  # the shared user-assigned identity's AcrPull role grant, not the
  # registry's built-in admin username/password.
  admin_enabled = false
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.shared.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.acr_pull.principal_id
}

data "azuread_service_principal" "ci" {
  client_id = var.ci_app_client_id
}

# Azure SQL's azuread_administrator only accepts a user or group, not a
# bare service principal, so the CI app's SP is added as a group member
# rather than named directly as the AAD admin (tasks 10/14/17 wire this
# group as each service's SQL server's azuread_administrator).
resource "azuread_group" "sql_admins" {
  display_name     = "sql-admins-order-system"
  security_enabled = true
  members          = [data.azuread_service_principal.ci.object_id]
}
