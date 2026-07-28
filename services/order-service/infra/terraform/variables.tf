variable "image_tag" {
  description = <<-EOT
    Docker image tag to deploy — the commit SHA that CI's docker-build-push job pushed to ACR.
    Defaults to "latest" only so a first bootstrap `terraform apply` (before any image has been
    built) doesn't fail on a missing variable; CI always passes the real SHA explicitly.
  EOT
  type        = string
  default     = "latest"
}

variable "sql_location" {
  description = <<-EOT
    Azure region for this service's SQL server + database — deliberately separate from the
    shared foundation's location (uksouth). This subscription's Microsoft.Sql resource provider
    rejects new server creation in uksouth with "ProvisioningDisabled ... Subscriptions are
    restricted from provisioning in this region"; spaincentral was confirmed to work via the
    Azure portal. A resource group's location is only metadata for the group itself — child
    resources can live in a different region, so only the SQL server/database need this
    override, not the whole shared foundation (which already has resources live in uksouth).
  EOT
  type        = string
  default     = "spaincentral"
}
