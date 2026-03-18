import { type ReactNode } from 'react';
import { usePermissions } from '../auth/usePermissions';

interface PermissionGateProps {
  permission?: string;
  anyOf?: string[];
  allOf?: string[];
  fallback?: ReactNode;
  children: ReactNode;
}

/**
 * Conditionally renders children based on the current user's permissions.
 * If no permission props are provided, children are always rendered.
 */
export function PermissionGate({
  permission,
  anyOf,
  allOf,
  fallback = null,
  children,
}: PermissionGateProps) {
  const { hasPermission, hasAnyPermission, hasAllPermissions } = usePermissions();

  let allowed = true;

  if (permission) {
    allowed = hasPermission(permission);
  } else if (anyOf && anyOf.length > 0) {
    allowed = hasAnyPermission(...anyOf);
  } else if (allOf && allOf.length > 0) {
    allowed = hasAllPermissions(...allOf);
  }

  if (!allowed) return <>{fallback}</>;
  return <>{children}</>;
}

/**
 * Full-page unauthorized fallback for permission-gated routes.
 */
export function UnauthorizedPage() {
  return (
    <div className="page-shell" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
      <div style={{ textAlign: 'center', color: 'var(--text-secondary)' }}>
        <h2 style={{ color: 'var(--text-primary)', marginBottom: 'var(--space-2)' }}>Access Denied</h2>
        <p>You do not have permission to view this page.</p>
        <p style={{ fontSize: 'var(--font-size-sm)', color: 'var(--text-faint)', marginTop: 'var(--space-2)' }}>
          Contact your administrator if you believe this is an error.
        </p>
      </div>
    </div>
  );
}
