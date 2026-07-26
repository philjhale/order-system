variable "location" {
  description = "Azure region for all shared foundation resources."
  type        = string
  default     = "uksouth"
}

variable "ci_app_client_id" {
  description = <<-EOT
    Client (application) ID of the "order-system-ci" Azure AD app
    registration created in task 4's manual bootstrap step
    (infra/terraform-bootstrap/README.md, section 4). Its service
    principal is looked up by this ID and added as a member of the
    SQL AAD-admin group, since Azure SQL's azuread_administrator block
    only accepts a user or group, not a bare service principal.
  EOT
  type        = string
  default     = "d55f413a-4ddb-4d49-b380-e87f02985c33"
}
