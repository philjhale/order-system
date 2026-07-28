locals {
  resource_group_name = data.terraform_remote_state.shared.outputs.resource_group_name
  location            = data.terraform_remote_state.shared.outputs.location

  image = "${data.terraform_remote_state.shared.outputs.container_registry_login_server}/order-service:${var.image_tag}"

  sql_server_fqdn   = azurerm_mssql_server.order_service.fully_qualified_domain_name
  sql_database_name = azurerm_mssql_database.order_service.name

  # AAD-only auth via this service's own user-assigned identity — no password, no stored
  # connection secret. Works for both the running Container App and the migration job, since
  # both have that identity attached.
  order_db_connection_string = "Server=tcp:${local.sql_server_fqdn},1433;Database=${local.sql_database_name};Authentication=Active Directory Managed Identity;User Id=${azurerm_user_assigned_identity.order_service.client_id};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

  servicebus_fully_qualified_namespace = "${data.terraform_remote_state.shared.outputs.servicebus_namespace_name}.servicebus.windows.net"
}

# Dedicated to this service — separate from the shared foundation's ACR-pull identity, since Service Bus
# data-plane RBAC and the SQL contained-user grant are both per-service, not environment-wide.
# Attached to both the always-running Container App and the migration job (below), so the
# db_ddladmin role the migration job's contained-user grant includes (required for EF Core's
# DDL migrations) is also held by the running app, not just the one-shot job — a least-privilege
# gap accepted for MVP simplicity (one identity per service, not two) rather than an oversight;
# revisit with a second, migration-only identity if this moves beyond MVP.
resource "azurerm_user_assigned_identity" "order_service" {
  name                = "id-order-service"
  resource_group_name = local.resource_group_name
  location            = local.location
}

resource "azurerm_role_assignment" "order_service_servicebus_data_owner" {
  scope                = data.terraform_remote_state.shared.outputs.servicebus_namespace_id
  role_definition_name = "Azure Service Bus Data Owner"
  principal_id         = azurerm_user_assigned_identity.order_service.principal_id
}

# Azure-AD-only auth (no SQL-auth admin/password anywhere) — sql-admins-order-system (the
# shared foundation's SQL AAD-admin group) is the server's AAD admin; this service's own
# identity is added as a contained DB user by the
# migration job (below), not by Terraform, since the GitHub-hosted runner has no network path to
# the server (firewall only allows Azure services).
#
# location is var.sql_location, not local.location (the shared foundation's uksouth) — see
# variables.tf for why this one resource needs its own region.
resource "azurerm_mssql_server" "order_service" {
  name                = "sql-order-service"
  resource_group_name = local.resource_group_name
  location            = var.sql_location
  version             = "12.0"

  azuread_administrator {
    login_username              = data.terraform_remote_state.shared.outputs.sql_admins_group_display_name
    object_id                   = data.terraform_remote_state.shared.outputs.sql_admins_group_object_id
    azuread_authentication_only = true
  }

  # Losing this server means losing every order ever placed — not recoverable from Terraform
  # state alone.
  lifecycle {
    prevent_destroy = true
  }
}

# Deliberately broad (any Azure-hosted resource, not just this service's own Container App or
# migration job) rather than a private endpoint — an accepted MVP tradeoff, not an oversight,
# since AAD-only auth is still required to actually connect. Same rationale as skipping Key
# Vault: revisit if this moves beyond MVP.
resource "azurerm_mssql_firewall_rule" "allow_azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.order_service.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_mssql_database" "order_service" {
  name      = "order-service"
  server_id = azurerm_mssql_server.order_service.id

  # Serverless: auto-pauses when idle to keep MVP cost down (services/order-service's
  # EnableRetryOnFailure() exists specifically to survive the resume latency this
  # causes).
  sku_name                    = "GP_S_Gen5_1"
  min_capacity                = 0.5
  auto_pause_delay_in_minutes = 60

  # Provider default is "Geo" (geo-redundant backup storage), which spaincentral doesn't
  # support ("ProvisioningDisabled: Provisioning of geo-redundant storage is not available in
  # this region") — Local (LRS) is also the cheaper, adequate choice for this MVP.
  storage_account_type = "Local"

  lifecycle {
    prevent_destroy = true
  }
}

# Owned by Order Service — Inventory/Payment/Fulfillment's own subscriptions to these are added
# in each of those services' own Terraform once they exist, not here.
resource "azurerm_servicebus_topic" "order_created" {
  name         = "OrderCreated"
  namespace_id = data.terraform_remote_state.shared.outputs.servicebus_namespace_id
}

resource "azurerm_servicebus_topic" "order_cancelled" {
  name         = "OrderCancelled"
  namespace_id = data.terraform_remote_state.shared.outputs.servicebus_namespace_id
}

resource "azurerm_servicebus_topic" "order_confirmed" {
  name         = "OrderConfirmed"
  namespace_id = data.terraform_remote_state.shared.outputs.servicebus_namespace_id
}

resource "azurerm_container_app" "order_service" {
  name                         = "ca-order-service"
  resource_group_name          = local.resource_group_name
  container_app_environment_id = data.terraform_remote_state.shared.outputs.container_app_environment_id
  revision_mode                = "Single"

  identity {
    type = "UserAssigned"
    identity_ids = [
      data.terraform_remote_state.shared.outputs.acr_pull_identity_id,
      azurerm_user_assigned_identity.order_service.id,
    ]
  }

  registry {
    server   = data.terraform_remote_state.shared.outputs.container_registry_login_server
    identity = data.terraform_remote_state.shared.outputs.acr_pull_identity_id
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # Order Service also consumes Service Bus events on subscriptions Container Apps'
    # default HTTP-driven scaling has no way to wake for — without a floor of 1, the app would
    # scale to zero between HTTP calls and never process InventoryReserved/PaymentCompleted/etc.
    min_replicas = 1

    container {
      name   = "order-service"
      image  = local.image
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ConnectionStrings__OrderDb"
        value = local.order_db_connection_string
      }

      env {
        name  = "ServiceBus__FullyQualifiedNamespace"
        value = local.servicebus_fully_qualified_namespace
      }

      # /health round-trips to SQL (services/order-service's OrderDbHealthCheck) rather than
      # just accepting a TCP connection, so a replica whose DB connection is broken (Serverless
      # auto-pause resume failure, missing contained user, etc.) gets taken out of rotation
      # instead of serving 500s.
      liveness_probe {
        transport = "HTTP"
        path      = "/health"
        port      = 8080
      }

      readiness_probe {
        transport = "HTTP"
        path      = "/health"
        port      = 8080
      }
    }
  }
}

# Runs `dotnet OrderSystem.OrderService.dll migrate` (contained-user provisioning + EF Core
# migrations) inside Azure, since neither step is reachable from the GitHub-hosted CI runner.
# CI triggers this via `az containerapp job start`, overriding Sql__CiAccessToken with a
# freshly-fetched token on every invocation (see .github/workflows/ci.yml).
resource "azurerm_container_app_job" "order_service_migrate" {
  name                         = "caj-order-service-migrate"
  resource_group_name          = local.resource_group_name
  location                     = local.location
  container_app_environment_id = data.terraform_remote_state.shared.outputs.container_app_environment_id

  # A freshly-idle Serverless DB's resume window plus a full `dotnet ef database update` run can
  # take longer than the default timeout.
  replica_timeout_in_seconds = 600

  manual_trigger_config {
    parallelism              = 1
    replica_completion_count = 1
  }

  identity {
    type = "UserAssigned"
    identity_ids = [
      data.terraform_remote_state.shared.outputs.acr_pull_identity_id,
      azurerm_user_assigned_identity.order_service.id,
    ]
  }

  registry {
    server   = data.terraform_remote_state.shared.outputs.container_registry_login_server
    identity = data.terraform_remote_state.shared.outputs.acr_pull_identity_id
  }

  template {
    container {
      name    = "migrate"
      image   = local.image
      cpu     = 0.5
      memory  = "1Gi"
      command = ["dotnet", "OrderSystem.OrderService.dll", "migrate"]

      env {
        name  = "ConnectionStrings__OrderDb"
        value = local.order_db_connection_string
      }

      env {
        name  = "Sql__ServerFqdn"
        value = local.sql_server_fqdn
      }

      env {
        name  = "Sql__DatabaseName"
        value = local.sql_database_name
      }

      env {
        name  = "Sql__ManagedIdentityName"
        value = azurerm_user_assigned_identity.order_service.name
      }

      # Placeholder — never actually used to connect. CI always overrides this with a real
      # short-lived token via `az containerapp job start --env-vars` before triggering a run.
      env {
        name  = "Sql__CiAccessToken"
        value = "unset"
      }
    }
  }
}
