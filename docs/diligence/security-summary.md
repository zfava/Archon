# ArchonAI — Security Summary

## For: Security Reviewers, Compliance Teams, CISOs

This document maps ArchonAI's security posture to automated evidence. Every control is linked to specific tests or configuration. Gaps are explicitly documented.

---

## Security Posture Overview

| Category | Controls Tested | Controls Untested | Automated Tests |
|---|---|---|---|
| Authentication | 10 attack vectors blocked | SSO/OIDC, MFA | 22 |
| Authorization (RBAC) | 7 controls verified | — | 23 |
| Tenant Isolation | 8 controls enforced | Database-level row isolation | 22 |
| Input Validation | 16 injection patterns handled | — | 16 |
| Configuration Security | 6 defaults verified | Secret rotation | 11 |
| Governance | 5 controls enforced | — | 16 |
| Audit Integrity | 4 controls verified | Persistent storage durability | 10 |

**Total: 56 security controls tested across 120 automated tests.**

---

## Authentication Security

### JWT Attack Vector Coverage

| # | Attack | Test | Status |
|---|---|---|---|
| 1 | Token forged with wrong signing key | `AuthBypassTests.ForgedToken_WrongSigningKey_Rejected` | Blocked |
| 2 | Payload tampered after signing | `AuthBypassTests.ManipulatedPayload_InvalidSignature_Rejected` | Blocked |
| 3 | Expired token replay | `AuthBypassTests.ExpiredToken_Rejected` | Blocked |
| 4 | Future-dated token (nbf bypass) | `AuthBypassTests.FutureToken_NotYetValid_Rejected` | Blocked |
| 5 | Wrong issuer | `AuthBypassTests.WrongIssuer_Rejected` | Blocked |
| 6 | Wrong audience | `AuthBypassTests.WrongAudience_Rejected` | Blocked |
| 7 | Empty token | `AuthBypassTests.EmptyToken_Rejected` | Blocked |
| 8 | Garbage token | `AuthBypassTests.GarbageToken_Rejected` | Blocked |
| 9 | "none" algorithm bypass | `AuthBypassTests.NoneAlgorithm_Rejected` | Blocked |
| 10 | Role elevation in token | `AuthBypassTests.RoleInToken_CannotBeElevated_WithoutReissue` | Blocked |

### Session Management

| Control | Test | Status |
|---|---|---|
| Refresh token rotation | `AuthSessionE2ETests.RefreshTokenRotation_ChainedRefreshes_EachProducesNewToken` | Enforced |
| Used refresh token invalidation | `AuthSessionE2ETests.FullLifecycle_Register_Login_Refresh_Logout` | Enforced |
| Logout revokes tokens | `AuthSessionE2ETests.FullLifecycle_Register_Login_Refresh_Logout` | Enforced |
| Duplicate email prevention | `AuthSessionE2ETests.DuplicateEmail_Registration_Rejected` | Enforced |

### Authentication Configuration

| Setting | Value | Source |
|---|---|---|
| Signing algorithm | HMAC-SHA256 | `appsettings.json` |
| Minimum key length | 32 bytes (enforced in Gateway startup) | `Program.cs` |
| Access token lifetime | 30 minutes | `Authentication:AccessTokenLifetimeMinutes` |
| Refresh token lifetime | 7 days | `Authentication:RefreshTokenLifetimeDays` |
| Clock skew tolerance | 60 seconds | `Security:Jwt:ClockSkewSeconds` |

---

## Authorization (RBAC)

### Role Hierarchy

| Role | Permissions | Protection |
|---|---|---|
| Admin | All 16 permissions | Immutable system role — cannot be modified or deleted |
| Operator | Execute + read permissions, no admin write | Immutable system role |
| Viewer | Read-only | Immutable system role |

### RBAC Controls

| Control | Test | Status |
|---|---|---|
| Admin has all permissions | `RbacIntegrationTests.AdminRole_HasAllPermissions` | Verified |
| Viewer restricted to read-only | `RbacIntegrationTests.ViewerRole_LacksWritePermissions` | Verified |
| Operator lacks admin access | `PermissionBoundaryTests.OperatorUser_LacksAdminWritePermission` | Verified |
| Unassigned user denied all | `PermissionBoundaryTests.UnassignedSubject_DeniedAll` | Verified |
| Revoked user loses access | `PermissionBoundaryTests.RevokedUser_LosesAllAccess` | Verified |
| Deny policy overrides allow | `RbacIntegrationTests.DenyPolicy_OverridesAllowPermission` | Verified |
| System roles immutable | `PermissionBoundaryTests.SystemRole_CannotBeUpdated/Deleted` | Enforced |

---

## Tenant Isolation

### Cross-Tenant Attack Prevention

| Attack Vector | Test | Status |
|---|---|---|
| Cross-tenant approval read | `CrossTenantAccessTests.ApprovalGate_CrossTenantRead_ReturnsNull` | Blocked |
| Cross-tenant approval review | `CrossTenantAccessTests.ApprovalGate_CrossTenantReview_Rejected` | Blocked |
| Cross-tenant history access | `CrossTenantAccessTests.ApprovalHistory_CrossTenant_ReturnsEmpty` | Blocked |
| Tenant context leakage under concurrency | `CrossTenantAccessTests.TenantContext_ConcurrentTenants_NoLeakage` | Blocked |
| Cross-tenant resource quota interference | `CrossTenantAccessTests.ResourceGovernor_TenantALimit_DoesNotAffectTenantB` | Blocked |
| Cross-tenant workflow execution | `WorkflowGovernanceE2ETests.WorkflowExecution_TenantIsolated` | Blocked |
| Pending approvals leak across tenants | `CrossTenantAccessTests.PendingApprovals_FilterByTenant` | Blocked |
| Concurrent scope isolation (50 parallel tasks) | `MultiTenantContextTests.ConcurrentScopes_AreIsolated` | Verified |

### Isolation Mechanism

Tenant isolation uses `AsyncLocal<string?>` for zero-allocation context propagation. Each request establishes a tenant scope that flows through the entire async call chain. Scope disposal restores the previous tenant context. Nested scopes are supported.

**Gap:** Database-level row isolation is not implemented. In-memory stores provide logical isolation only.

---

## Input Validation

### Injection Prevention

| Attack Type | Variants Tested | Test Suite | Status |
|---|---|---|---|
| SQL injection in email | 4 (`OR '1'='1`, `DROP TABLE`, `admin'--`, `UNION SELECT`) | `InputValidationTests` | Safe |
| SQL injection in password | 2 (`OR '1'='1`, `OR 1=1 --`) | `InputValidationTests` | Safe |
| XSS in organization name | 3 (`<script>`, `<img onerror>`, `javascript:`) | `InputValidationTests` | Safe |
| XSS in display name | 2 (`<script>`, template injection `{{constructor}}`) | `InputValidationTests` | Safe |
| Null byte injection | 1 (`admin\0@test.com`) | `InputValidationTests` | Safe |

### Boundary Handling

| Scenario | Test | Status |
|---|---|---|
| Empty email | `InputValidationTests.EmptyEmail_Login_ReturnsNull` | Handled |
| Empty password | `InputValidationTests.EmptyPassword_Login_ReturnsNull` | Handled |
| 10K character email | `InputValidationTests.VeryLongEmail_Login_DoesNotCrash` | Handled |
| 100K character password | `InputValidationTests.VeryLongPassword_Login_DoesNotCrash` | Handled |
| Unicode/i18n emails (4 variants) | `InputValidationTests.SpecialCharacterEmails_HandleGracefully` | Handled |

---

## Governance & Separation of Duties

| Control | Test | Status |
|---|---|---|
| Critical actions require approval | `InsecureConfigurationTests.GovernanceDefaults_CriticalActions_RequireApproval` | Enforced |
| Self-approval blocked | `PermissionBoundaryTests.SeparationOfDuties_RequesterCannotSelfApprove` | Enforced |
| Insufficient role cannot approve | `PermissionBoundaryTests.InsufficientRole_CannotApprove` | Enforced |
| Approval → execution flow | `WorkflowGovernanceE2ETests.WorkflowCancel_RequiresApproval_ThenExecutes` | Verified |
| Denied approval stops execution | `WorkflowGovernanceE2ETests.ApprovalDenied_WorkflowNotCancelled` | Verified |

---

## Configuration Security

### Secure Defaults

| Default | Expected | Test | Status |
|---|---|---|---|
| Confidence threshold | ≥ 0.5 (actual: 0.65) | `InsecureConfigurationTests.PolicyDefaults_ConfidenceThreshold_IsReasonable` | Verified |
| Auto-block threshold | Set (actual: 80) | `InsecureConfigurationTests.PolicyDefaults_AutoBlockThreshold_IsSet` | Verified |
| Approval threshold < auto-block | Set (actual: 60 < 80) | `InsecureConfigurationTests.PolicyDefaults_ApprovalThreshold_IsSet` | Verified |
| High-risk requires approval | Enabled | `InsecureConfigurationTests.PolicyDefaults_RequiresApprovalForHighRisk` | Verified |
| Tenant plan limits bounded | ≤ 100 | `InsecureConfigurationTests.TenantDefaults_MaxConcurrentPlans_IsLimited` | Verified |
| Critical actions gated | Enabled | `InsecureConfigurationTests.GovernanceDefaults_CriticalActions_RequireApproval` | Verified |

### Rate Limiting

| Policy | Limit | Queue Depth |
|---|---|---|
| Standard API | 120 requests/minute | 20 |
| Admin API | 60 requests/minute | 10 |
| Connectors | Custom per-connector | — |

### Network Security

| Control | Configured | Tested Under Load |
|---|---|---|
| Rate limiting | Yes (YARP fixed window) | No |
| CORS | Yes (Gateway middleware) | No |
| TLS | Configured in Helm (port 443) | No |
| Network policies | Not configured | No |

---

## Audit Trail Integrity

| Control | Test | Status |
|---|---|---|
| SHA-256 hash chain | `AuditLogIntegrationTests.RecordAsync_LinkedHashChain_PreviousEntryIdSet` | Verified |
| Chain integrity verification | `AuditLogIntegrationTests.VerifyIntegrity_PassesForValidChain` | Verified |
| Category-based statistics | `AuditLogIntegrationTests.Status_TracksCategoryCounts` | Verified |
| Query filtering | `AuditLogIntegrationTests.Query*` (category, subject, time range) | Verified |

**Gap:** Audit entries stored in `ConcurrentDictionary`. Not persisted across restarts. No tamper-evident external storage.

---

## Unverified Security Areas

### Critical (P0)

| Area | Current State | Risk |
|---|---|---|
| **SSO/OIDC** | Not implemented. JWT issuance exists but no IdP integration. | Enterprise deployments typically require Okta/Entra/Auth0. |
| **MFA** | Not implemented. | Compliance blocker for regulated industries. |
| **Secret management** | API keys, JWT signing keys, database passwords in plaintext `appsettings.json` and Helm values. | Credential exposure in source control and container images. |

### High (P1)

| Area | Current State | Risk |
|---|---|---|
| **Database persistence** | Core state in-memory. | Audit log durability claim not proven for production. |
| **Load testing** | No performance tests. | Unknown behavior under concurrent load. |
| **Database connection encryption** | Not configured in connection strings. | Data in transit exposure. |

### Medium (P2)

| Area | Current State | Risk |
|---|---|---|
| **Container security scanning** | No automated CVE checking. | Supply chain vulnerability exposure. |
| **Dependency vulnerability scanning** | No `dotnet list package --vulnerable` pipeline. | Known-CVE risk in transitive dependencies. |
| **Circuit breaker** | Retry logic exists. No circuit breaker under sustained failure. | Cascading failure risk in connector layer. |
| **CORS under adversarial conditions** | Configuration exists. No adversarial testing. | Browser-context attacks. |

---

## Compliance Readiness

| Framework | Readiness | Key Gaps |
|---|---|---|
| **SOC 2 Type II** | Partial — audit trail, RBAC, access controls exist | No persistent audit storage, no SSO, secrets in plaintext |
| **ISO 27001** | Partial — access control (A.9), cryptography (A.10) present | No asset management, no incident management, no risk treatment |
| **GDPR** | Minimal — tenant isolation provides data segregation | No data subject access request flow, no right-to-erasure |
| **HIPAA** | Not ready | No encryption at rest, no BAA support, no PHI handling controls |

---

## How to Verify

```bash
# Run all security tests (< 2 seconds)
cd archonai
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "Namespace~Security" --verbosity normal

# Run auth bypass tests specifically
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "FullyQualifiedName~AuthBypassTests"

# Run cross-tenant isolation tests
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "FullyQualifiedName~CrossTenantAccessTests"

# Run input validation tests
dotnet test tests/ArchonAI.Enterprise.Tests/ --filter "FullyQualifiedName~InputValidationTests"
```
