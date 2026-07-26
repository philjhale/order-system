output "container_app_fqdn" {
  value = azurerm_container_app.order_service.latest_revision_fqdn
}

output "managed_identity_client_id" {
  value = azurerm_user_assigned_identity.order_service.client_id
}

output "sql_server_fqdn" {
  value = azurerm_mssql_server.order_service.fully_qualified_domain_name
}

output "container_app_job_name" {
  value = azurerm_container_app_job.order_service_migrate.name
}
