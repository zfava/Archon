# AI Validation and Safety

## Output Validation

`ModelOutputValidator` provides post-generation validation for AI responses when structured output is expected.

### Validation Pipeline

```
ModelResponse
      │
      ├── Is schema requested? (ResponseJsonSchema != null)
      │     No → return as-is
      │     Yes ▼
      ├── Parse as JSON
      │     Success → validate against schema
      │     Failure ▼
      ├── Attempt repair
      │     ├── Extract from markdown code blocks
      │     ├── Remove trailing commas
      │     ├── Re-parse
      │     Success → validate + add JSON_REPAIRED warning
      │     Failure → SchemaValid=false + JSON_INVALID warning
      │
      └── Schema validation (required field presence check)
            ├── All required fields present → SchemaValid=true
            └── Missing fields → SchemaValid=false + SCHEMA_MISSING_FIELD warnings
```

### Warning Categories

| Warning | Meaning |
|---------|---------|
| `JSON_REPAIRED` | Output was invalid JSON but was successfully repaired |
| `JSON_INVALID` | Output is not JSON and could not be repaired |
| `SCHEMA_MISSING_FIELD: {field}` | Required field absent from response |
| `SCHEMA_MISMATCH` | Expected JSON object, got different type |
| `SCHEMA_PARSE_ERROR` | Validation schema itself is malformed |
| `DETERMINISTIC_ECHO` | Response is not AI-generated (local fallback) |

### Repair Strategies

1. **Markdown extraction**: LLMs often wrap JSON in ` ```json ``` ` blocks
2. **Trailing comma removal**: Common LLM error in arrays and objects
3. More strategies can be added to `AttemptJsonRepair()` as patterns are observed

### Caller Responsibility

Validation metadata is informational. Callers must decide how to handle:
- `SchemaValid=true` → safe to deserialize and use
- `SchemaValid=false` → log warning, request retry, or fall back to default
- `IsSuccess=false` → provider-level failure, no content to validate

## Safety Invariants

1. **No silent success on failure**: Every provider failure returns `IsSuccess=false` with specific error
2. **No mocked success paths**: Echo fallback is explicitly labeled `echo_fallback`
3. **All invocations observable**: CorrelationId traces every request end-to-end
4. **All invocations attributable**: `RequestedBy` identifies the calling agent/service
5. **Token spend tracked**: Usage returned from providers, cost estimated via capability registry
6. **Content filter visibility**: Provider-level content filtering reported via `FinishReason` and warnings

## Provider Failure Modes

| Failure | Behavior |
|---------|----------|
| Missing API key | Immediate error, no HTTP call attempted |
| HTTP 429 (rate limit) | Retry with exponential backoff (3 attempts) |
| HTTP 5xx (server error) | Retry with exponential backoff (3 attempts) |
| HTTP 4xx (client error) | Immediate failure with error message |
| Timeout | Fail after configured timeout, report latency |
| Caller cancellation | Propagate immediately, no retry |
| Malformed response | Parse error with details |
| Local server unreachable | Echo fallback with DETERMINISTIC_ECHO warning |
