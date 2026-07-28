output "container_app_fqdn" {
  value = azurerm_container_app.order_service.latest_revision_fqdn
}

output "managed_identity_client_id" {
  value = azurerm_user_assigned_identity.order_service.client_id
}

output "sql_server_fqdn" {
  value = azurerm_mssql_server.order_service.fully_qualified_domain_name
}

output "sql_database_name" {
  value = azurerm_mssql_database.order_service.name
}

output "managed_identity_name" {
  value = azurerm_user_assigned_identity.order_service.name
}

output "container_app_job_name" {
  value = azurerm_container_app_job.order_service_migrate.name
}

output "image" {
  value = local.image
}

# Needed once, manually, to grant this identity the Directory Readers Entra ID role — required
# for the SQL server to resolve external-provider principals (see main.tf). Not something
# Terraform/CI can do: assigning a directory role needs Privileged Role Administrator or Global
# Administrator, which the CI service principal doesn't (and shouldn't) have.
output "sql_server_identity_principal_id" {
  value = azurerm_mssql_server.order_service.identity[0].principal_id
}
