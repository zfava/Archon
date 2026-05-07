# Permissions Matrix

## Role-Permission Mapping

| Permission | Admin | Operator | Viewer |
|------------|:-----:|:--------:|:------:|
| `agents:read` | Y | Y | Y |
| `agents:write` | Y | - | - |
| `agents:execute` | Y | Y | - |
| `workflows:read` | Y | Y | Y |
| `workflows:write` | Y | - | - |
| `workflows:execute` | Y | Y | - |
| `connectors:read` | Y | Y | Y |
| `connectors:write` | Y | - | - |
| `connectors:execute` | Y | Y | - |
| `admin:read` | Y | - | - |
| `admin:write` | Y | - | - |
| `policy:read` | Y | Y | Y |
| `policy:write` | Y | - | - |
| `monitoring:read` | Y | Y | Y |
| `rbac:read` | Y | Y | Y |
| `rbac:write` | Y | - | - |
| `governance:read` | Y | Y | Y |
| `governance:write` | Y | - | - |
| `governance:approve` | Y | - | - |

## Permission Format

Permissions follow the pattern `resource:action`:
- **resource**: The domain area (e.g., `agents`, `workflows`, `governance`)
- **action**: The operation type (`read`, `write`, `execute`, `approve`)

## Custom Roles

Admins can create custom roles via `POST /api/v1/rbac/roles` with any subset of permissions. Custom roles are assigned to users/agents via `POST /api/v1/rbac/roles/assign` and evaluated alongside JWT role claims.

## Route-to-Permission Mapping

| Route Pattern | Required Policy | Minimum Role |
|---------------|----------------|--------------|
| `GET /api/v1/health` | OperatorOrAdmin | Operator |
| `GET /api/v1/registry/*` | OperatorOrAdmin | Operator |
| `POST /api/v1/goals/*` | OperatorOrAdmin | Operator |
| `GET /api/v1/control-plane/*` | OperatorOrAdmin | Operator |
| `POST /api/v1/control-plane/policies` | AdminOnly | Admin |
| `DELETE /api/v1/control-plane/policies/*` | AdminOnly | Admin |
| `*/rbac/*` (write operations) | AdminOnly | Admin |
| `GET /api/v1/governance/*` | GovernanceRead | Viewer |
| `POST /api/v1/governance/request` | OperatorOrAdmin | Operator |
| `POST /api/v1/governance/*/review` | GovernanceApprove | Admin |
| `POST /api/v1/governance/policies` | GovernanceWrite | Admin |
