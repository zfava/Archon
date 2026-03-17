# AI Provider Architecture

## Overview

ArchonAI uses a pluggable, multi-provider AI execution architecture with adaptive routing, performance tracking, and automatic weight adjustment. All AI invocations flow through a single `IModelProvider` abstraction backed by the `CompositeModelProvider` which delegates to provider-specific adapters.

## Provider Stack

```
Agent / Service
      │
      ▼
IModelProvider (CompositeModelProvider)
      │
      ├── IModelRouter (routing decision)
      │
      ├── OpenAiModelProvider      ── HTTP → api.openai.com/v1/chat/completions
      ├── AnthropicModelProvider   ── HTTP → api.anthropic.com/v1/messages
      ├── AzureOpenAiModelProvider ── HTTP → {endpoint}/openai/deployments/{dep}/chat/completions
      └── LocalModelProvider       ── HTTP → localhost:11434/api/generate (Ollama)
                                        └── echo fallback if server unreachable
```

## Provider Adapters

| Provider | Status | API Format | Auth |
|----------|--------|------------|------|
| OpenAI | Real HTTP | Chat Completions v1 | Bearer token |
| Anthropic | Real HTTP | Messages v1 | x-api-key header |
| Azure OpenAI | Real HTTP | Azure Chat Completions | api-key header |
| Local (Ollama) | Real HTTP with echo fallback | Ollama generate API | None |

### Configuration

```json
{
  "ModelProviders": {
    "DefaultModel": "local.default",
    "OpenAI": { "ApiKey": "<key>", "Endpoint": "https://api.openai.com" },
    "Anthropic": { "ApiKey": "<key>", "Endpoint": "https://api.anthropic.com" },
    "AzureOpenAI": { "ApiKey": "<key>", "Endpoint": "<url>", "Deployment": "gpt-4o-mini" },
    "Local": { "Endpoint": "http://localhost:11434", "DefaultModel": "local.default" }
  }
}
```

API keys must be provided via secure configuration or environment variables. Providers with missing keys return explicit errors — never silent fallbacks.

## Model Capability Registry

`ModelCapabilityRegistry` provides a static registry of known models with:
- Maximum context/output tokens
- JSON mode support
- Vision support
- Per-token cost estimates (input/output)

Used by the router for cost estimation and by accounting for spend tracking.

## Routing

The `IModelRouter` selects a provider/model based on:
1. Explicit model in request
2. Task-type weighted routing (adaptive)
3. Task-type static mapping
4. Strategy-based routing (cost/latency/quality)
5. Fallback chains

See `ModelRouterOptions` for full configuration.

## Resilience

Each provider implements:
- **Retry**: Up to 3 attempts with exponential backoff for 429/5xx errors
- **Timeout**: 60s default, overridable per-request via `ModelRequest.Timeout`
- **Cancellation**: Caller cancellation propagates immediately without retry
- **Error translation**: Provider-specific HTTP errors mapped to `ModelResponse.Errors`

## Observability

Every request carries a `CorrelationId` (auto-generated UUID) propagated through:
- Request → Router → Provider → Response
- Performance tracker recording
- Structured logging

## Token Accounting

`ModelResponse.Usage` returns `TokenUsage(PromptTokens, CompletionTokens, TotalTokens)` from provider responses. The `CompositeModelProvider` uses `ModelCapabilityRegistry` cost data to estimate per-request spend for performance tracking.

## Extension Points

To add a new provider (e.g., Google Gemini):
1. Implement `IModelProvider` in `ArchonAI.Models`
2. Register in `DependencyInjection.AddArchonAIModels()`
3. Add to `CompositeModelProvider` constructor
4. Add model entries to `ModelCapabilityRegistry`
5. Add named `HttpClient` registration
