# ArchonAI — Azure Quickstart Deployment

This Terraform configuration provisions the minimum Azure infrastructure needed to run the ArchonAI API.

## What It Provisions

| Resource | Purpose |
|----------|---------|
| Azure Resource Group | Logical container for all resources |
| Azure Database for PostgreSQL Flexible Server | v16, 2 vCores (GP_Standard_D2s_v3), 32 GB storage |
| Azure Container Apps Environment | Managed container orchestration |
| Azure Container App | Runs the ArchonAI API (from `Dockerfile.api`) |
| Azure Key Vault | Secrets management |
| Azure Log Analytics Workspace | Container logs and metrics |

## Prerequisites

- [Terraform](https://developer.hashicorp.com/terraform/downloads) >= 1.5.0
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli) authenticated (`az login`)
- A Docker image of the ArchonAI API pushed to a container registry

## Usage

1. **Initialize Terraform:**
   ```bash
   cd deploy/terraform
   terraform init
   ```

2. **Create a `terraform.tfvars` file** (do NOT commit this):
   ```hcl
   postgresql_admin_password = "your-secure-password"
   jwt_signing_key           = "your-jwt-signing-key"
   api_image_tag             = "ghcr.io/archonai/api:0.12.0"
   location                  = "eastus2"
   ```

3. **Plan and apply:**
   ```bash
   terraform plan
   terraform apply
   ```

4. **Verify deployment:**
   ```bash
   terraform output api_url
   curl "$(terraform output -raw api_url)/healthz/live"
   ```

## Production Considerations

This is a quickstart template. For production deployments, consider:

- **Private networking**: VNet integration, private endpoints for PostgreSQL and Key Vault
- **WAF**: Azure Front Door or Application Gateway with WAF policies
- **Backup**: Automated PostgreSQL backups with geo-redundant storage
- **Auto-scaling**: Container App scaling rules based on HTTP concurrency or CPU
- **Monitoring**: Azure Monitor alerts, Application Insights integration
- **Disaster recovery**: Multi-region deployment with traffic manager
- **TLS**: Custom domain with managed certificate
