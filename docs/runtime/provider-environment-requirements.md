# Provider Environment Requirements

This document specifies the environment variables and configuration required for ArchonAI AI providers.

## Required Environment Variables

### Cloud Providers (Production)

| Variable | Provider | Required For |
|----------|----------|-------------|
| `OPENAI_API_KEY` | OpenAI | GPT-4.1, GPT-4.1-mini models |
| `ANTHROPIC_API_KEY` | Anthropic | Claude Sonnet 4.6, Claude Opus 4.6, Claude Haiku 4.5 models |
| `AZURE_OPENAI_API_KEY` | Azure OpenAI | Azure-hosted GPT models |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI | Required alongside Azure API key |

### Minimum Production Configuration

At least **one** cloud provider API key must be set:

```bash
# Option A: OpenAI
export OPENAI_API_KEY="sk-..."

# Option B: Anthropic
export ANTHROPIC_API_KEY="sk-ant-..."

# Option C: Azure OpenAI
export AZURE_OPENAI_API_KEY="..."
export AZURE_OPENAI_ENDPOINT="https://your-instance.openai.azure.com"
```

### Recommended Production Configuration

Set **two or more** cloud provider keys for redundancy:

```bash
export OPENAI_API_KEY="sk-..."
export ANTHROPIC_API_KEY="sk-ant-..."
```

## Configuration Precedence

1. Environment variables (highest priority)
2. `appsettings.{Environment}.json`
3. `appsettings.json` (lowest priority)

Environment variables always override configuration file values. This is enforced by `PostConfigure` in `DependencyInjection.cs`.

## Provider Configuration Options

### appsettings.json Structure

```json
{
  "ModelProviders": {
    "DefaultModel": "openai.gpt-4.1-mini",
    "OpenAI": {
      "Enabled": true,
      "Endpoint": "https://api.openai.com"
    },
    "Anthropic": {
      "Enabled": true,
      "Endpoint": "https://api.anthropic.com"
    },
    "AzureOpenAI": {
      "Enabled": false,
      "Deployment": "gpt-4o-mini"
    },
    "Local": {
      "Enabled": true,
      "Endpoint": "http://localhost:11434",
      "DefaultModel": "local.default"
    }
  }
}
```

**Note**: API keys should never be stored in configuration files. Always use environment variables or a secrets manager.

## Readiness Tiers

| Tier | Criteria | Suitable For |
|------|----------|-------------|
| `production` | 2+ cloud providers configured | Production, enterprise pilots |
| `production-single` | 1 cloud provider configured | Development, staging |
| `local-only` | Only local (Ollama) available | Local development only |
| `unconfigured` | Zero providers active | Broken — all AI requests will fail |

## Startup Behavior

The `ModelProviderActivationService` validates configuration at startup and logs the result:

- **`production` tier**: `INFO` log confirming redundant provider availability
- **`local-only` tier**: `WARNING` log advising to configure cloud providers
- **`unconfigured` tier**: `CRITICAL` log stating all AI endpoints will return errors

## Verifying Configuration

### Via API

```bash
curl -s http://localhost:5000/api/v1/ai-runtime/environment | jq .
```

### Via Script

```bash
./scripts/verify-ai-runtime.sh http://localhost:5000
```

### Via Tests

```bash
dotnet test --filter "Category=RuntimeProof&Subsystem=AiRuntime"
```
