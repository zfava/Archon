# AI Execution Model

## Request Lifecycle

```
1. Agent creates ModelRequest with prompt, model hint, correlation ID
2. CompositeModelProvider receives request
3. ModelRouter selects provider/model based on routing strategy
4. Request is normalized with routing metadata
5. Selected provider makes HTTP call to external API
6. Provider parses response, extracts content + token usage
7. CompositeModelProvider records outcome to performance tracker
8. Response returned with correlation ID, usage, latency, finish reason
```

## ModelRequest

| Field | Type | Description |
|-------|------|-------------|
| Model | string | Logical model name (e.g. "openai.gpt-4.1") |
| Prompt | string | User/agent prompt |
| Parameters | dict | Routing hints, task type, metadata |
| RequestedBy | string | Caller identity for attribution |
| CorrelationId | string | Auto-generated trace ID |
| SystemPrompt | string? | Optional system instructions |
| MaxTokens | int? | Max output tokens |
| Temperature | double? | Sampling temperature |
| ResponseJsonSchema | string? | Expected JSON schema for validation |
| Timeout | TimeSpan? | Per-request timeout override |

## ModelResponse

| Field | Type | Description |
|-------|------|-------------|
| Provider | string | Provider that handled the request |
| Model | string | Model that generated the response |
| IsSuccess | bool | Whether the call succeeded |
| Content | string | Generated text |
| Warnings | list | Truncation, filter, or repair warnings |
| Errors | list | Error messages if IsSuccess=false |
| CorrelationId | string | Matching request correlation ID |
| Usage | TokenUsage? | Prompt/completion/total token counts |
| LatencyMs | double? | Wall-clock latency |
| FinishReason | string? | stop, length, content_filter, echo_fallback |
| SchemaValid | bool? | JSON schema validation result |

## Flow Classification

| Behavior | Label | Example |
|----------|-------|---------|
| Real provider HTTP call | AI-generated | OpenAI, Anthropic, Azure, Ollama responses |
| Local echo fallback | DETERMINISTIC_ECHO | Local provider when Ollama server unreachable |
| Rules-based logic | Not AI | RBAC checks, governance policy evaluation |

Every response from the local echo fallback includes:
- `FinishReason = "echo_fallback"`
- Warning: `"DETERMINISTIC_ECHO: Local model server unavailable."`

## Error Handling

- Missing API key → immediate `IsSuccess=false` with descriptive error
- HTTP 4xx/5xx → retry (429, 5xx) or fail with translated error message
- Timeout → `IsSuccess=false` with latency reported
- Invalid response body → `IsSuccess=false` with parse error details
- No errors are silently swallowed

## Performance Tracking Integration

The `CompositeModelProvider` automatically records every invocation outcome:
- Provider, model, success/failure
- Latency in milliseconds
- Estimated cost from capability registry
- Task type (if provided in request parameters)

This feeds the adaptive routing weight engine for automatic provider optimization.
