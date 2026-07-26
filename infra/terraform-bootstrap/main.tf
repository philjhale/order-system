terraform {
  required_version = ">= 1.7"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }

  # Local state deliberately: this config creates the remote-state backend
  # that every other Terraform config in this repo uses, so it can't use
  # that backend itself. Run once by hand (see README).
}

provider "azurerm" {
  features {}
}

resource "azurerm_resource_group" "bootstrap" {
  name     = "rg-order-system-bootstrap"
  location = "uksouth"
}

resource "azurerm_storage_account" "tfstate" {
  name                     = "stordersystemtfstate01"
  resource_group_name      = azurerm_resource_group.bootstrap.name
  location                 = azurerm_resource_group.bootstrap.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  blob_properties {
    versioning_enabled = true
  }
}

resource "azurerm_storage_container" "tfstate" {
  name                  = "tfstate"
  storage_account_name  = azurerm_storage_account.tfstate.name
  container_access_type = "private"
}
