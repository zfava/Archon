variable "resource_group_name" {
  description = "Name of the Azure Resource Group"
  type        = string
  default     = "archonai-quickstart"
}

variable "location" {
  description = "Azure region for all resources"
  type        = string
  default     = "eastus2"
}

variable "postgresql_admin_password" {
  description = "Administrator password for PostgreSQL Flexible Server"
  type        = string
  sensitive   = true
}

variable "api_image_tag" {
  description = "Docker image tag for the ArchonAI API (e.g., ghcr.io/archonai/api:0.12.0)"
  type        = string
  default     = "ghcr.io/archonai/api:latest"
}

variable "jwt_signing_key" {
  description = "JWT signing key for API authentication"
  type        = string
  sensitive   = true
}
