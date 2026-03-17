# Tenancy Model

## Overview

ArchonAI implements a logical multi-tenant architecture where each tenant is an **Organization**. All data access is scoped by tenant ID, which is propagated from the authenticated user's JWT claims through middleware.

## Domain Model

```
Organization (tenant)
 ├── Users (via Membership)
 ├── Policies
 ├── Configurations
 ├── Goals / Strategies / Task Graphs
 └── Audit Entries
```

### Organization

| Field | Type | Description |
|-------|------|-------------|
| Id | GUID | Primary key, used as `tenant_id` |
| Name | string | Display name |
| Slug | string | URL-safe identifier (unique) |
| IsActive | bool | Soft-delete / suspension flag |
| CreatedAtUtc | DateTimeOffset | Creation timestamp |

### User (UserIdentity)

| Field | Type | Description |
|-------|------|-------------|
| Id | GUID | Primary key |
| Email | string | Unique login identifier |
| DisplayName | string | Display name |
| PasswordHash | string | PBKDF2 hash |
| OrganizationId | GUID | Primary org (default tenant) |
| Role | string | Default role |
| IsActive | bool | Account status |

### Membership

| Field | Type | Description |
|-------|------|-------------|
| Id | GUID | Primary key |
| UserId | GUID | FK to User |
| OrganizationId | GUID | FK to Organization |
| Role | string | Role within this org |
| JoinedAtUtc | DateTimeOffset | When user joined |

A user can have memberships in multiple organizations (future: org switching).

## Tenant Resolution

```
Request → JWT Authentication → TenantResolutionMiddleware
                                    │
                                    ▼
                            Extract "tenant_id" claim
                                    │
                                    ▼
                            IMultiTenantContext.BeginTenantScope(tenantId)
                                    │
                                    ▼
                            All downstream services read tenant from context
```

The `TenantResolutionMiddleware` runs after `UseAuthentication()` and before route handlers. It reads the `tenant_id` claim from the JWT and activates a scoped tenant context.

## Roles

| Role | Scope | Description |
|------|-------|-------------|
| Admin | Organization | Full access. Can invite users, manage policies. |
| Operator | Organization | Can execute workflows, approve goals, view data. |
| Viewer | Organization | Read-only access to dashboards and reports. |

Roles are stored on the `Membership` record and included in the JWT `role` claim.

## Invite Flow

```
Admin calls POST /api/auth/invite { email, role }
    │
    ▼
System generates invite token (256-bit, 7-day expiry)
    │
    ▼
Invite token shared with invitee (email delivery out of scope)
    │
    ▼
Invitee calls POST /api/auth/accept-invite { inviteToken, password, displayName }
    │
    ▼
System creates user + membership in the inviting org
    │
    ▼
Returns access + refresh tokens (user is immediately logged in)
```

## Data Isolation

- **Store-level**: All store interfaces accept org/tenant IDs. `ListByOrganizationAsync` filters by org.
- **Middleware-level**: `IMultiTenantContext` scope ensures downstream services only see current tenant data.
- **JWT-level**: `tenant_id` and `org_id` claims are set at token issuance and cannot be modified client-side.

## Future Considerations

- **Org switching**: Allow users with multiple memberships to switch active org context
- **Hierarchical tenancy**: Parent/child org relationships for enterprise accounts
- **Data partitioning**: Move from logical to physical isolation for compliance
- **SSO per-tenant**: Per-org OIDC/SAML configuration
