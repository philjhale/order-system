variable "image_tag" {
  description = <<-EOT
    Docker image tag to deploy — the commit SHA that CI's docker-build-push job pushed to ACR.
    Defaults to "latest" only so a first bootstrap `terraform apply` (before any image has been
    built) doesn't fail on a missing variable; CI always passes the real SHA explicitly.
  EOT
  type        = string
  default     = "latest"
}
