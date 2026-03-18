# Admin Console UX

## Overview

The ArchonAI admin console provides enterprise operators and administrators with operational visibility, governance controls, and system management capabilities. All admin surfaces enforce role-based access control and are designed for operator clarity.

## Page Inventory

### Operations Pages

| Page | Route | Description | Min Role |
|------|-------|-------------|----------|
| Command Console | `/` | Natural language operations interface | Viewer |
| System Activity | `/activity` | Real-time agent and task monitoring (SignalR) | Viewer |
| Business Impact | `/impact` | KPI dashboard, before/after metrics, cost savings | Viewer |
| Control Panel | `/control` | Execution modes, department rules, security policies | Operator |
| Integrations | `/integrations` | Connector marketplace, connect/disconnect | Operator |
| Strategy View | `/strategy/:goalId` | Task graph visualization, strategy details | Viewer |
| Attribution | `/attribution/:goalId` | Outcome attribution, knowledge graph | Viewer |
| Explanations | `/explanations` | Decision explainer, agent selection reasoning | Viewer |

### Administration Pages

| Page | Route | Description | Min Role |
|------|-------|-------------|----------|
| Audit Log | `/audit` | Immutable event log, integrity verification | Operator |
| Human Overrides | `/overrides` | Pause/resume/cancel/rollback workflows | Operator |
| Organization | `/admin/org` | Org details, member listing, roles reference | Admin |
| System Health | `/admin/health` | Service status, connector health, credential warnings | Operator |

## Navigation

The persistent sidebar (AppShell) organizes pages into two sections:

**Operations** — Day-to-day operational pages visible to all authenticated users (subject to permission checks).

**Administration** — Governance and system management pages, visible only to users with appropriate permissions.

Nav items are automatically hidden when the user lacks the required permission.

## Async State Handling

Every page that loads data follows a consistent pattern:

1. **Loading** — Spinner with contextual message
2. **Error** — Red banner with error message and retry button
3. **Empty** — Informative placeholder with icon, title, and description
4. **Data** — Actual content

The `AsyncBoundary` component handles this uniformly:

```tsx
<AsyncBoundary
  loading={state.loading}
  error={state.error}
  empty={data.length === 0}
  emptyTitle="No workflows found"
  emptyDescription="Create a workflow from the Command Console"
  onRetry={reload}
>
  <DataTable data={data} />
</AsyncBoundary>
```

## Key UX Patterns

### Permission Gates

UI elements are conditionally rendered based on permissions:

```tsx
<PermissionGate permission="admin:write">
  <button>Delete Organization</button>
</PermissionGate>

<PermissionGate anyOf={['governance:approve', 'admin:write']}>
  <ApprovalQueue />
</PermissionGate>
```

Unauthorized route access shows an "Access Denied" page instead of broken UI.

### Tenant Awareness

The sidebar displays the current tenant (organization) name. All API calls are scoped to the authenticated user's tenant via the JWT token.

### Error Classification

Error messages shown to users are human-readable. Raw API errors are caught and displayed in error banners with retry actions where appropriate.

### Real-Time Updates

Pages using SignalR show connection status indicators:
- **Connected** — Green dot, live data flowing
- **Reconnecting** — Amber dot, attempting reconnection
- **Disconnected** — Red dot, data may be stale

## Organization Admin (`/admin/org`)

Displays:
- Organization details (name, slug, ID)
- Roles reference card (Admin, Operator, Viewer with descriptions)
- Member listing table (name, email, role, last active)
- Invite member button (Admin only, requires backend endpoint)

## System Health (`/admin/health`)

Displays:
- Overall system status indicator (Healthy/Degraded/Unhealthy)
- Core services health grid (API Server, Event Bus, Workflow Engine)
- Connector health table:
  - Status with color-coded dots
  - Authentication state
  - Request/error counts
  - Rate limit remaining
  - Credential expiry warnings (amber badge)
- Manual refresh button

## Audit Log (`/audit`)

Displays:
- Pagination (50 entries/page) with offset controls
- Server-side filters: category, subject, resource type
- Client-side text search across description, event type, action
- Integrity verification button with result display
- Audit status summary (total entries, last entry time)

## Control Panel (`/control`)

Displays:
- Execution mode selector (Observe / Recommend / Execute)
- Department rules with approval requirements, cost/risk limits
- Security policy management
- Tenant selector (for multi-tenant operators)
- Governance status indicators

## Human Overrides (`/overrides`)

Displays:
- Override action buttons: Pause, Resume, Cancel, Modify Strategy, Rollback
- Workflow filter
- Override log table with timestamps, actions, results
- Contextual feedback on action success/failure
