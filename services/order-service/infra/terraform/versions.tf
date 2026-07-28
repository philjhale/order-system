terraform {
  required_version = ">= 1.7"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 2.0"
    }
  }

  backend "azurerm" {
    resource_group_name  = "rg-order-system-bootstrap"
    storage_account_name = "stordersystemtfstate01"
    container_name       = "tfstate"
    key                  = "order-service.tfstate"
  }
}

provider "azurerm" {
  features {}
}

provider "azuread" {}
