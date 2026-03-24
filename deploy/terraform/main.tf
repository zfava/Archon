# ArchonAI — Azure Quickstart Deployment
# This Terraform configuration provisions the minimum Azure infrastructure
# needed to run the ArchonAI API. It is a quickstart template, not a
# production-hardened deployment. For production, additional considerations
# (private networking, WAF, backup policies, auto-scaling) would apply.

terraform {
  required_version = ">= 1.5.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
}

provider "azurerm" {
  features {}
}

# ── Resource Group ─────────────────────────────────────────────────────────────
resource "azurerm_resource_group" "archonai" {
  name     = var.resource_group_name
  location = var.location

  tags = {
    project     = "ArchonAI"
    environment = "quickstart"
  }
}

# ── Azure Key Vault (secrets management) ───────────────────────────────────────
data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "archonai" {
  name                       = "${var.resource_group_name}-kv"
  location                   = azurerm_resource_group.archonai.location
  resource_group_name        = azurerm_resource_group.archonai.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  purge_protection_enabled   = false

  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = data.azurerm_client_config.current.object_id

    secret_permissions = [
      "Get", "List", "Set", "Delete", "Purge",
    ]
  }

  tags = azurerm_resource_group.archonai.tags
}

# ── Azure Database for PostgreSQL Flexible Server ──────────────────────────────
resource "azurerm_postgresql_flexible_server" "archonai" {
  name                          = "${var.resource_group_name}-pg"
  resource_group_name           = azurerm_resource_group.archonai.name
  location                      = azurerm_resource_group.archonai.location
  version                       = "16"
  administrator_login           = "archonai_admin"
  administrator_password        = var.postgresql_admin_password
  storage_mb                    = 32768
  sku_name                      = "GP_Standard_D2s_v3" # 2 vCores, 4 GB RAM

  tags = azurerm_resource_group.archonai.tags
}

# Allow Azure services to connect to PostgreSQL
resource "azurerm_postgresql_flexible_server_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.archonai.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Create the application database
resource "azurerm_postgresql_flexible_server_database" "archonai" {
  name      = "archonai"
  server_id = azurerm_postgresql_flexible_server.archonai.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# ── Azure Container Apps Environment ──────────────────────────────────────────
resource "azurerm_log_analytics_workspace" "archonai" {
  name                = "${var.resource_group_name}-logs"
  location            = azurerm_resource_group.archonai.location
  resource_group_name = azurerm_resource_group.archonai.name
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = azurerm_resource_group.archonai.tags
}

resource "azurerm_container_app_environment" "archonai" {
  name                       = "${var.resource_group_name}-env"
  location                   = azurerm_resource_group.archonai.location
  resource_group_name        = azurerm_resource_group.archonai.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.archonai.id

  tags = azurerm_resource_group.archonai.tags
}

# ── Azure Container App (API) ─────────────────────────────────────────────────
# Runs the ArchonAI API built from archonai/deploy/docker/Dockerfile.api
resource "azurerm_container_app" "api" {
  name                         = "${var.resource_group_name}-api"
  container_app_environment_id = azurerm_container_app_environment.archonai.id
  resource_group_name          = azurerm_resource_group.archonai.name
  revision_mode                = "Single"

  template {
    min_replicas = 1
    max_replicas = 3

    container {
      name   = "archonai-api"
      image  = var.api_image_tag
      cpu    = 1.0
      memory = "2Gi"

      # Environment variables for the API
      env {
        name  = "ASPNETCORE_URLS"
        value = "http://+:8080"
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name        = "ARCHONAI_JWT_SIGNING_KEY"
        secret_name = "jwt-signing-key"
      }

      env {
        name = "ConnectionStrings__Persistence"
        value = join("", [
          "Host=${azurerm_postgresql_flexible_server.archonai.fqdn};",
          "Port=5432;",
          "Database=archonai;",
          "Username=archonai_admin;",
          "Password=${var.postgresql_admin_password};",
          "SSL Mode=Require;",
        ])
      }

      # Liveness probe
      liveness_probe {
        path             = "/healthz/live"
        port             = 8080
        transport        = "HTTP"
        initial_delay    = 10
        interval_seconds = 30
      }

      # Readiness probe
      readiness_probe {
        path             = "/healthz/ready"
        port             = 8080
        transport        = "HTTP"
        initial_delay    = 15
        interval_seconds = 10
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  secret {
    name  = "jwt-signing-key"
    value = var.jwt_signing_key
  }

  tags = azurerm_resource_group.archonai.tags
}
