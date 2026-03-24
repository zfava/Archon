# AI Runtime Proof

This document describes how ArchonAI proves that AI agent execution is backed by real model providers, not stubs or echo responses.

## Runtime Proof Architecture

### 1. Startup Provider Validation (`ModelProviderActivationService`)

On application startup, the `ModelProviderActivationService` hosted service:

- Enumerates all configured providers (OpenAI, Anthropic, AzureOpenAI, Local)
- Validates API key presence for each cloud provider
- Logs a structured status table to the application log
- Emits `archonai_model_providers_active` Prometheus gauge per provider
- Publishes a `model.providers.activated` event with full status payload
- Logs `CRITICAL` if zero providers are active
- Logs `WARNING` if only the local provider is available

### 2. Environment Diagnostics Endpoint

**`GET /api/v1/ai-runtime/environment`** returns a structured `EnvironmentReport`:

```json
{
  "checkedAtUtc": "2026-03-20T00:00:00Z",
  "defaultModel": "openai.gpt-4.1-mini",
  "readinessTier": "production",
  "readinessSummary": "2 cloud providers active with redundancy.",
  "cloudProvidersActive": 2,
  "localProviderActive": true,
  "providers": [
    { "name": "OpenAI", "providerType": "cloud", "status": "ready", ... },
    { "name": "Anthropic", "providerType": "cloud", "status": "ready", ... },
    ...
  ]
}
```

Readiness tiers:
- **`production`** — 2+ cloud providers with valid API keys
- **`production-single`** — 1 cloud provider (no redundancy)
- **`local-only`** — Only local model server (not suitable for production)
- **`unconfigured`** — Zero active providers (all AI requests will fail)

### 3. Live Smoke Test

**`POST /api/v1/ai-runtime/smoke-test`** sends a minimal prompt to each configured provider and reports:

- Provider name and model
- Pass/fail/skip/error status
- Latency in milliseconds
- Token usage (prompt, completion, total)
- Finish reason
- Error message if applicable

### 4. No-Echo Guarantee

All model providers enforce explicit failure when misconfigured:

- **Missing API key**: Returns `IsSuccess: false` with a clear error message and empty content
- **Unreachable server**: Returns `IsSuccess: false` with `provider_unavailable` finish reason
- **No echo fallback**: The `CompositeModelProvider` actively detects and logs `echo_fallback` finish reasons as `CRITICAL`
- **Empty content on failure**: All error responses set `Content = string.Empty` to prevent any data leakage

### 5. Telemetry

Every model invocation emits:
- `archonai.model.invocations.total` (counter, tagged by provider/model/success)
- `archonai.model.invocations.failed` (counter)
- `archonai.model.invocation.duration.ms` (histogram)
- `archonai.model.invocation.tokens.input` (histogram)
- `archonai.model.invocation.tokens.output` (histogram)
- `archonai.model.invocation.tokens.total` (counter)

## Verification

### Automated Tests

Run the AI runtime proof test suite:

```bash
dotnet test --filter "Category=RuntimeProof&Subsystem=AiRuntime"
```

### Manual Verification Script

```bash
./scripts/verify-ai-runtime.sh http://localhost:5000
```

### What the Tests Prove

| Test | What It Proves |
|------|---------------|
| `EnvironmentReport_NoKeys_ReturnsUnconfiguredTier` | Zero-config is explicitly detected |
| `EnvironmentReport_TwoCloudProviders_ReturnsProductionTier` | Production readiness correctly classified |
| `SmokeTest_NoKeys_AllSkippedOrFailed` | No fake passes when keys are missing |
| `NoProvider_NeverEchoesUserInput` | No echo/stub behavior under any failure mode |
| `OpenAi_MissingKey_ReturnsStructuredError_NoEcho` | Explicit, honest error for missing OpenAI key |
| `Local_Unavailable_ReturnsStructuredError_NoEcho` | Local failure is explicit, not masked |
