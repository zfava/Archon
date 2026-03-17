# Authorization Model

## Overview

ArchonAI implements a hybrid authorization model combining:
1. **JWT role claims** — Fast, stateless permission checks derived from the user's role
2. **RBAC store assignments** — Dynamic, fine-grained permissions via `IRbacService`
3. **Policy-based authorization** — ASP.NET Core policies mapping to permission requirements

All three layers are evaluated by `PermissionRequirementHandler`. JWT role claims are checked first (fast path), then RBAC store assignments (for custom roles, agent subjects, etc.).

## Permission Resolution Flow

```
Request with JWT
    │
    ▼
PermissionRequirementHandler
    │
    ├─ 1. Extract "role" claim from JWT
    │     └─ Map role → permission set (Admin/Operator/Viewer)
    │        └─ If required permission is in set → ALLOW
    │
    └─ 2. Query IRbacService.EvaluateAccessAsync(userId, resource, action)
          └─ Check role assignments in store
             └─ Evaluate permission policies (allow/deny)
                └─ If allowed → ALLOW, else → DENY
```

## Authorization Policies

| Policy Name | Requirement | Used By |
|-------------|-------------|---------|
| (fallback) | Authenticated user | All endpoints |
| `AdminOnly` | Role = Admin | RBAC management, security config |
| `OperatorOrAdmin` | Role in {Operator, Admin} | Most operational endpoints |
| `ViewerOrAbove` | Role in {Viewer, Operator, Admin} | Read-only dashboards |
| `AgentRead` | Permission `agents:read` | Agent listing |
| `AgentWrite` | Permission `agents:write` | Agent creation/modification |
| `ConnectorAccess` | Permission `connectors:execute` | Integration execution |
| `PolicyManagement` | Permission `policy:write` | Policy CRUD |
| `RbacManagement` | Permission `rbac:write` | Role/assignment CRUD |
| `GovernanceRead` | Permission `governance:read` | View approval gates |
| `GovernanceWrite` | Permission `governance:write` | Create approval policies |
| `GovernanceApprove` | Permission `governance:approve` | Approve/deny requests |
| `TenantScoped` | `tenant_id` matches route param | Cross-tenant prevention |

## Tenant-Scoped Authorization

`TenantMatchRequirementHandler` enforces that a user's `tenant_id` JWT claim matches any `tenantId` route parameter or query parameter in the request. This prevents cross-tenant data access at the authorization layer.

## Frontend Permission Awareness

The `usePermissions()` hook provides:
- `hasPermission(perm)` — Check a single permission
- `hasAnyPermission(...perms)` — Check if any permission is held
- `hasAllPermissions(...perms)` — Check if all permissions are held
- `isAdmin`, `isOperator`, `isViewer` — Role convenience flags

Use these to conditionally render UI elements:
```tsx
const { hasPermission } = usePermissions();
{hasPermission('governance:approve') && <ApproveButton />}
```

## Permission Introspection

`GET /api/v1/auth/permissions` returns the current user's effective permissions (merged from JWT role and RBAC store). This allows the frontend to dynamically enable/disable features.
