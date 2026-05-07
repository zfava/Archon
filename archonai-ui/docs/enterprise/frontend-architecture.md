# Frontend Architecture

## Overview

ArchonAI's frontend is a React 19 single-page application built with TypeScript 5.9, Vite 8, and React Router 7. It provides an enterprise operator and admin console for managing AI-driven operations.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | React 19.2 |
| Language | TypeScript 5.9 (strict mode) |
| Routing | React Router DOM 7.13 |
| Build | Vite 8 |
| Real-time | Microsoft SignalR 10 |
| Styling | Custom CSS design system (dark theme, CSS variables) |
| State | React Context + Hooks (no external state library) |

## Project Structure

```
src/
├── api/
│   └── client.ts              # REST API client with auth injection
├── auth/
│   ├── AuthContext.tsx          # Auth state provider (JWT, session)
│   ├── AuthApiWiring.tsx        # Wires auth into API client
│   ├── LoginPage.tsx            # Login/register UI
│   ├── ProtectedRoute.tsx       # Route guard (redirects to /login)
│   ├── usePermissions.ts        # Role-based permission hook
│   └── index.ts                 # Barrel export
├── shell/
│   ├── AppShell.tsx             # Persistent nav sidebar + main content area
│   └── app-shell.css            # Shell layout styles
├── shared/
│   ├── AsyncState.tsx           # LoadingState, ErrorState, EmptyState, AsyncBoundary
│   ├── PermissionGate.tsx       # Permission-aware conditional rendering
│   └── useTelemetry.ts          # Frontend telemetry hook (beacon API)
├── features/
│   ├── command/                 # Command console (main interface)
│   ├── activity/                # Real-time system activity (SignalR)
│   ├── impact/                  # Business impact dashboard
│   ├── control/                 # Execution control panel
│   ├── audit/                   # Immutable audit log
│   ├── strategy/                # Strategy visualization
│   ├── attribution/             # Outcome attribution
│   ├── explanations/            # Decision explainer
│   ├── overrides/               # Human intervention log
│   ├── integrations/            # Connector marketplace
│   ├── onboarding/              # Setup wizard
│   └── admin/                   # Org admin, system health
├── index.css                    # Design system tokens & global styles
└── App.tsx                      # Root router with shell integration
```

## Authentication & Authorization

### Auth Flow

1. User submits credentials to `/api/auth/login`
2. Server returns JWT access token, refresh token, expiry
3. Tokens stored in `localStorage` (access, refresh, expiry)
4. Session restored on page load via `/api/auth/me`
5. Token auto-refreshed 1 minute before expiry
6. 401 responses trigger automatic logout

### Role-Based Access Control

Three roles with additive permissions:

| Role | Key Permissions |
|------|----------------|
| Admin | Full access: `admin:write`, `rbac:write`, `governance:approve`, all Operator perms |
| Operator | Execute workflows, read policies, monitor system |
| Viewer | Read-only access to all operational data |

### Permission-Aware UI

- **`PermissionGate`** — conditionally renders children based on role permissions
- **`usePermissions`** — hook exposing `hasPermission()`, `hasAnyPermission()`, `hasAllPermissions()`
- **AppShell sidebar** — nav items hidden when user lacks required permission
- **Route-level gating** — `AdminGated` wrapper redirects unauthorized users to an access denied page

## App Shell

The `AppShell` component wraps all authenticated routes with:

- **Persistent sidebar** with permission-gated navigation
- **Tenant display** — shows current organization name
- **User profile** — avatar, display name, role badge
- **Logout control** — in sidebar footer
- **Two nav sections**: Operations (Command, Activity, Impact, Control, Integrations) and Administration (Audit, Overrides, Organization, System Health)

## Async State Management

The `AsyncBoundary` component standardizes loading/error/empty states:

```tsx
<AsyncBoundary
  loading={state.loading}
  error={state.error}
  empty={items.length === 0}
  emptyTitle="No results"
  onRetry={reload}
>
  {/* Content rendered only when data is loaded */}
</AsyncBoundary>
```

Individual components also available: `LoadingState`, `ErrorState`, `EmptyState`.

## API Client

The `api` client (`src/api/client.ts`) provides:

- Centralized base URL configuration
- Automatic Bearer token injection
- Automatic 401 handling (triggers logout)
- Typed endpoint groups: Goals, Strategy, Task Graphs, Economics, Control Plane, Audit, Observability, Integrations, Overrides, Explanations

## Real-Time Updates

SignalR hubs at `/hubs/control-plane-dashboard` provide live data for:
- System Activity dashboard
- Impact dashboard
- Module-level updates (agent activity, task performance, system health)

Connection management includes automatic reconnection with backoff: `[0, 2000, 5000, 10000, 30000]ms`.

## Telemetry

The `useTelemetry` hook provides lightweight frontend telemetry:
- Page view tracking with timing
- User action tracking
- Error event capture
- Performance measurement
- Events batched (10 events or 30s) and shipped via `navigator.sendBeacon`

## Design System

CSS variables define the design language:

- **Surface**: `--bg-root` through `--bg-hover` (dark theme)
- **Text**: `--text-primary` through `--text-faint`
- **Accent**: `--accent` (indigo `#6366f1`)
- **Semantic**: `--success`, `--warning`, `--error`
- **Typography**: Inter font family, `--font-size-xs` through `--font-size-3xl`
- **Spacing**: `--space-1` through `--space-6`
- **Radii**: `--radius-sm` through `--radius-2xl`

No CSS preprocessor or utility framework — plain CSS with variables and scoped feature styles.
