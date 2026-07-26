output "resource_group_name" {
  value = azurerm_resource_group.shared.name
}

output "location" {
  value = azurerm_resource_group.shared.location
}

output "container_app_environment_id" {
  value = azurerm_container_app_environment.shared.id
}

output "servicebus_namespace_id" {
  value = azurerm_servicebus_namespace.shared.id
}

output "servicebus_namespace_name" {
  value = azurerm_servicebus_namespace.shared.name
}

output "container_registry_id" {
  value = azurerm_container_registry.shared.id
}

output "container_registry_login_server" {
  value = azurerm_container_registry.shared.login_server
}

output "acr_pull_identity_id" {
  value = azurerm_user_assigned_identity.acr_pull.id
}

output "acr_pull_identity_client_id" {
  value = azurerm_user_assigned_identity.acr_pull.client_id
}

output "acr_pull_identity_principal_id" {
  value = azurerm_user_assigned_identity.acr_pull.principal_id
}

output "sql_admins_group_object_id" {
  value = azuread_group.sql_admins.object_id
}
