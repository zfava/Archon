import { useMemo } from 'react';
import { useAuth } from './AuthContext';

const ROLE_PERMISSIONS: Record<string, readonly string[]> = {
  Admin: [
    'agents:read', 'agents:write', 'agents:execute',
    'workflows:read', 'workflows:write', 'workflows:execute',
    'connectors:read', 'connectors:write', 'connectors:execute',
    'admin:read', 'admin:write',
    'policy:read', 'policy:write',
    'monitoring:read',
    'rbac:read', 'rbac:write',
    'governance:read', 'governance:write', 'governance:approve',
  ],
  Operator: [
    'agents:read', 'agents:execute',
    'workflows:read', 'workflows:execute',
    'connectors:read', 'connectors:execute',
    'monitoring:read',
    'policy:read',
    'rbac:read',
    'governance:read',
  ],
  Viewer: [
    'agents:read',
    'workflows:read',
    'connectors:read',
    'monitoring:read',
    'policy:read',
    'rbac:read',
    'governance:read',
  ],
};

export function usePermissions() {
  const { user } = useAuth();

  const permissions = useMemo(() => {
    if (!user) return new Set<string>();
    return new Set(ROLE_PERMISSIONS[user.role] ?? []);
  }, [user]);

  return {
    permissions,
    hasPermission: (perm: string) => permissions.has(perm),
    hasAnyPermission: (...perms: string[]) => perms.some(p => permissions.has(p)),
    hasAllPermissions: (...perms: string[]) => perms.every(p => permissions.has(p)),
    role: user?.role ?? null,
    isAdmin: user?.role === 'Admin',
    isOperator: user?.role === 'Operator',
    isViewer: user?.role === 'Viewer',
  };
}
