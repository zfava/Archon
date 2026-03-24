output "api_url" {
  description = "The public URL of the ArchonAI API"
  value       = "https://${azurerm_container_app.api.ingress[0].fqdn}"
}

output "postgresql_connection_string" {
  description = "PostgreSQL connection string (sensitive)"
  value = join("", [
    "Host=${azurerm_postgresql_flexible_server.archonai.fqdn};",
    "Port=5432;",
    "Database=archonai;",
    "Username=archonai_admin;",
    "Password=${var.postgresql_admin_password};",
    "SSL Mode=Require;",
  ])
  sensitive = true
}

output "key_vault_uri" {
  description = "Azure Key Vault URI for secrets management"
  value       = azurerm_key_vault.archonai.vault_uri
}

output "resource_group_name" {
  description = "Name of the provisioned resource group"
  value       = azurerm_resource_group.archonai.name
}
