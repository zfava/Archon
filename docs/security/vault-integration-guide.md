# Vault Integration Guide

## Overview

ArchonAI supports three external vault backends as `ISecretProvider` implementations. Each integrates into the `ChainedSecretProvider` chain, where the first provider to return a non-null value wins. This allows multiple providers to coexist (e.g., HashiCorp Vault primary + environment variable fallback).

All vault providers implement:
- `ISecretProvider` — secret retrieval with graceful degradation
- `ISecretRotationNotifier` — polling-based rotation detection with callback notifications
- Thread safety via `Lock` primitives
- Structured diagnostic logging at startup

## Provider Chain Priority

When multiple vault backends are configured, the chain order is:

1. **HashiCorp Vault** (if `Vault:Endpoint` is set)
2. **AWS Secrets Manager** (if `Aws:SecretsManager:Region` is set)
3. **Azure Key Vault** (if `Azure:KeyVault:VaultUri` is set)
4. **File-based** (if `Secrets:VaultPath` is set — Vault Agent sidecar mode)
5. **Environment variables** (always present as final fallback)

---

## HashiCorp Vault

### Configuration

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Vault:Endpoint` | Yes | — | Vault server URL (e.g., `http://vault:8200`) |
| `Vault:MountPath` | No | `secret` | KV v2 mount path |
| `Vault:AppRoleRoleId` | Yes | — | AppRole role ID |
| `Vault:AppRoleSecretId` | Yes | — | AppRole secret ID |
| `Vault:RenewIntervalSeconds` | No | `60` | Rotation polling interval |

### Authentication

Uses AppRole authentication (`POST /v1/auth/approle/login`). The provider:
1. Authenticates with role_id + secret_id at startup
2. Tracks token TTL from the lease_duration
3. Renews the token at 75% of the lease duration
4. Re-authenticates if renewal fails

### Secret Retrieval

Reads from KV v2: `GET /v1/{mountPath}/data/{secretPath}`.

### Rotation Detection

Polls for version changes every `RenewIntervalSeconds`. When a secret's version changes, all registered callbacks are notified with the secret key (never the value).

### Setup

```bash
# Enable AppRole auth
vault auth enable approle

# Create a policy
vault policy write archonai - <<EOF
path "secret/data/*" {
  capabilities = ["read", "list"]
}
EOF

# Create an AppRole
vault write auth/approle/role/archonai \
  token_policies="archonai" \
  token_ttl=1h \
  token_max_ttl=4h

# Get role_id and secret_id
vault read auth/approle/role/archonai/role-id
vault write -f auth/approle/role/archonai/secret-id

# Store a secret
vault kv put secret/ARCHONAI_JWT_SIGNING_KEY value="your-signing-key-here"
```

---

## AWS Secrets Manager

### Configuration

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Aws:SecretsManager:Region` | Yes | — | AWS region (e.g., `us-east-1`) |
| `Aws:SecretsManager:SecretNamePrefix` | No | `""` | Prefix for secret names |
| `Aws:SecretsManager:PollingIntervalSeconds` | No | `300` | Rotation polling interval |

### Authentication

Uses the AWS SDK default credential chain — no hardcoded credentials. In order of precedence:
1. Environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`)
2. AWS credentials file (`~/.aws/credentials`)
3. ECS container credentials
4. EC2 instance role
5. IAM Roles for Service Accounts (IRSA) on EKS

### Required IAM Permissions

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue",
        "secretsmanager:DescribeSecret"
      ],
      "Resource": "arn:aws:secretsmanager:*:*:secret:archonai/*"
    }
  ]
}
```

### Setup

```bash
# Create a secret
aws secretsmanager create-secret \
  --name archonai/ARCHONAI_JWT_SIGNING_KEY \
  --secret-string "your-signing-key-here" \
  --region us-east-1

# Enable automatic rotation (optional)
aws secretsmanager rotate-secret \
  --secret-id archonai/ARCHONAI_JWT_SIGNING_KEY \
  --rotation-lambda-arn arn:aws:lambda:us-east-1:123456789:function:rotate-archonai \
  --rotation-rules AutomaticallyAfterDays=30
```

### NuGet Dependency

Add to the Infrastructure project:
```xml
<PackageReference Include="AWSSDK.SecretsManager" Version="3.7.*" />
```

---

## Azure Key Vault

### Configuration

| Key | Required | Default | Description |
|-----|----------|---------|-------------|
| `Azure:KeyVault:VaultUri` | Yes | — | Key Vault URI (e.g., `https://my-vault.vault.azure.net`) |
| `Azure:KeyVault:PollingIntervalSeconds` | No | `300` | Rotation polling interval |

### Authentication

Uses `DefaultAzureCredential` (Managed Identity). In order of precedence:
1. Environment variables (`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`)
2. Managed Identity (System-assigned or User-assigned)
3. Visual Studio / VS Code credentials (development)
4. Azure CLI credentials (development)

### Required Azure RBAC

Assign the **Key Vault Secrets User** role to the application's managed identity:

```bash
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee <managed-identity-principal-id> \
  --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.KeyVault/vaults/<vault-name>
```

### Setup

```bash
# Create a Key Vault
az keyvault create \
  --name archonai-vault \
  --resource-group archonai-rg \
  --location eastus \
  --enable-rbac-authorization

# Store a secret (Azure Key Vault uses hyphens, not underscores)
az keyvault secret set \
  --vault-name archonai-vault \
  --name ARCHONAI-JWT-SIGNING-KEY \
  --value "your-signing-key-here"
```

### NuGet Dependencies

Add to the Infrastructure project:
```xml
<PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.6.*" />
<PackageReference Include="Azure.Identity" Version="1.12.*" />
```

---

## Graceful Degradation

All three vault providers follow the same degradation pattern:

1. **Transient failures** (network errors, timeouts, 5xx responses) → return `null`, allowing `ChainedSecretProvider` to try the next provider in the chain
2. **Configuration errors** (missing endpoint, missing auth config) → throw `InvalidOperationException` at startup
3. **SDK not available** (missing NuGet package) → log warning at startup, return `null` for all lookups

This means the application always starts, even if a vault is unreachable. Secrets fall through to environment variables as the last resort.

## Rotation Handling

When a vault provider detects a secret version change:

1. The `ISecretRotationNotifier` fires callbacks with the changed key
2. `RotatingJwtSecurityKeyProvider` listens for rotation events and rotates JWT signing keys with dual-key validation during rollover
3. The rollover window (default: 1 hour) ensures tokens signed with the previous key remain valid

## Testing

```bash
# Run vault provider unit tests
dotnet test archonai/tests/ArchonAI.Tests/ \
  --filter "FullyQualifiedName~Secrets" \
  --verbosity normal
```
