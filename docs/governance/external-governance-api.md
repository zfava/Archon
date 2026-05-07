# External Governance API

The External Governance API exposes ArchonAI's governance control plane — trust-tiered autonomy, risk scoring, HITL approval gates, and HMAC-signed policy decisions — to any external agentic system.

Base URL: `/api/v1/external/governance`

## Authentication

All external governance endpoints require an `X-Archon-Api-Key` header.

```
X-Archon-Api-Key: your-api-key-here
```

### API Key Provisioning

API keys are provisioned through the ArchonAI admin interface or via the `ISecretProvider` chain. Keys are stored as:

- **Per-key**: `external-api-key:<keyId>` → `<orgId>:<rateLimitTier>` (e.g., `org-uuid:premium`)
- **Shared key** (dev/bootstrap): `external-api-shared-key` → the shared key value (requires `X-Archon-Org-Id` header)

The `keyId` is derived as the first 16 hex characters of `SHA256(apiKey)`. The actual key value is never logged or stored in plaintext outside the secret provider.

### Claims Set on Authentication

| Claim | Description |
|-------|-------------|
| `org-id` | Organization ID bound to the API key |
| `api-key-id` | Derived key identifier (for audit logging) |
| `rate-limit-tier` | Rate limit tier (`standard`, `premium`, etc.) |
| `tenant_id` | Organization ID (for tenant isolation) |

## Rate Limiting

Requests are rate-limited per organization using a sliding window algorithm:

- **Default**: 60 requests per minute per organization
- **Window**: 1 minute with 6 segments (10-second granularity)
- **Configurable**: Set `ExternalApi:RateLimitPerMinute` in configuration

When rate-limited, the API returns:

```
HTTP 429 Too Many Requests
Retry-After: 60

{
  "error": "Rate limit exceeded. Retry after the period specified in Retry-After header."
}
```

## Endpoints

### POST /evaluate

Submit a task for governance evaluation. Returns a signed policy decision.

**Request:**

```json
{
  "taskDescription": "Transfer $50,000 from operating account",
  "requiredCapability": "financial.transfer",
  "actionScope": "financial.execute",
  "contextMetadata": {
    "department": "finance",
    "urgency": "normal"
  },
  "callerIdentity": "external-agent-001",
  "organizationId": "org-uuid"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `taskDescription` | string | Yes | Human-readable description of the task |
| `requiredCapability` | string | Yes | The capability required to execute the task |
| `actionScope` | string | Yes | The scope of the action (used for trust tier lookup) |
| `contextMetadata` | object | No | Additional key-value context for policy evaluation |
| `callerIdentity` | string | Yes | Identifier of the calling agent/system |
| `organizationId` | string | Yes | Organization ID for tenant scoping |

**Response (200 OK):**

```json
{
  "decision": "require-approval",
  "confidence": 0.72,
  "trustTierApplied": "DraftApprovalRequired",
  "riskScore": 65.0,
  "violations": [],
  "guardrailFindings": ["value-exceeds-auto-execute-threshold"],
  "approvalGateId": "a1b2c3d4-...",
  "evaluationId": "e5f6g7h8-...",
  "evaluatedAt": "2026-03-20T12:00:00.000Z",
  "signedDigest": "base64url-hmac-sha256"
}
```

**Decision values:**

| Decision | Meaning |
|----------|---------|
| `allow` | Task may proceed without human intervention |
| `require-approval` | Task requires human approval; poll the approval gate |
| `deny` | Task is denied by policy (forbidden capability, blocked) |

### GET /evaluation/{evaluationId}

Retrieve a previously submitted evaluation result.

**Response (200 OK):** Same as the `/evaluate` response.

**Response (404):**
```json
{ "error": "Evaluation not found." }
```

### POST /approval/{gateId}/status

Poll the approval status of a governance gate.

**Request:**
```json
{
  "callerIdentity": "external-agent-001"
}
```

**Response (200 OK):**
```json
{
  "gateId": "a1b2c3d4-...",
  "status": "Pending",
  "reviewedBy": null,
  "reviewNotes": null,
  "reviewedAtUtc": null
}
```

**Status values:** `Pending`, `Approved`, `Denied`, `Expired`

### GET /trust-tier/{actionScope}

Get the effective trust tier for a given action scope within the caller's organization.

**Response (200 OK):**
```json
{
  "actionScope": "financial.execute",
  "effectiveTier": "DraftApprovalRequired",
  "tierLevel": 2
}
```

**Trust tier levels:**

| Level | Name | Description |
|-------|------|-------------|
| 0 | ObserveOnly | System may observe and record but take no action |
| 1 | RecommendOnly | System may recommend but not draft or execute |
| 2 | DraftApprovalRequired | System may draft actions requiring human approval |
| 3 | AutoExecuteReversible | System may auto-execute reversible actions |
| 4 | AutoExecuteHighConfidence | System may auto-execute high-confidence bounded actions |
| 5 | PolicyEnvelope | Full autonomy within an approved policy envelope |

## Security Model

### HMAC-SHA256 Signed Evaluations

Every evaluation response includes a `signedDigest` field — an HMAC-SHA256 signature over the evaluation payload:

```
HMAC-SHA256(
  key: external-api-signing-key,
  data: "{evaluationId}:{decision}:{riskScore:F2}:{evaluatedAt:O}"
)
```

The digest is base64url-encoded (no padding). External systems can verify the digest by:

1. Obtaining the signing key from ArchonAI (out-of-band provisioning)
2. Reconstructing the payload string from the response fields
3. Computing HMAC-SHA256 and comparing with `signedDigest`

This prevents tampering with governance decisions in transit or at rest.

### Tenant Isolation

- API keys are bound to organizations
- Approval gates enforce tenant isolation — an organization cannot access another's gates
- All evaluation events are published to the audit trail with organization context

### Audit Trail

All evaluations are published to the `IEventBus` with event type `external.governance.evaluation`, including:

- `evaluationId`, `decision`, `riskScore`
- `callerIdentity`, `organizationId`, `actionScope`

## Error Codes

| HTTP Status | Meaning |
|------------|---------|
| 400 | Missing or invalid required fields |
| 401 | Missing or invalid API key |
| 404 | Evaluation or approval gate not found |
| 429 | Rate limit exceeded (check `Retry-After` header) |

## Integration Guide

### Quick Start

1. **Obtain an API key** from the ArchonAI admin interface
2. **Submit an evaluation** via `POST /evaluate` with your task details
3. **Check the decision**:
   - `allow` → proceed with execution
   - `deny` → abort; check `violations` for details
   - `require-approval` → poll `POST /approval/{gateId}/status` until resolved
4. **Verify the digest** to ensure the decision was not tampered with

### Example: Python Integration

```python
import requests
import hmac
import hashlib
import base64

API_URL = "https://archonai.example.com/api/v1/external/governance"
API_KEY = "your-api-key"

# Submit evaluation
response = requests.post(
    f"{API_URL}/evaluate",
    headers={"X-Archon-Api-Key": API_KEY},
    json={
        "taskDescription": "Transfer $50,000 from operating account",
        "requiredCapability": "financial.transfer",
        "actionScope": "financial.execute",
        "contextMetadata": {"department": "finance"},
        "callerIdentity": "my-agent-001",
        "organizationId": "org-uuid"
    }
)

result = response.json()

if result["decision"] == "allow":
    # Proceed with execution
    pass
elif result["decision"] == "require-approval":
    # Poll for approval
    gate_id = result["approvalGateId"]
    status = requests.post(
        f"{API_URL}/approval/{gate_id}/status",
        headers={"X-Archon-Api-Key": API_KEY},
        json={"callerIdentity": "my-agent-001"}
    ).json()
```

### Example: Verifying the Signed Digest

```python
def verify_digest(evaluation, signing_key):
    payload = f"{evaluation['evaluationId']}:{evaluation['decision']}:{evaluation['riskScore']:.2f}:{evaluation['evaluatedAt']}"
    expected = hmac.new(
        signing_key.encode(), payload.encode(), hashlib.sha256
    ).digest()
    expected_b64 = base64.urlsafe_b64encode(expected).rstrip(b"=").decode()
    return hmac.compare_digest(expected_b64, evaluation["signedDigest"])
```
