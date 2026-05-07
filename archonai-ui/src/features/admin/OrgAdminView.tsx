import { useCallback, useRef, useState, useEffect } from 'react';
import { useAuth } from '../../auth/AuthContext';
import { usePermissions } from '../../auth/usePermissions';
import { AsyncBoundary, EmptyState } from '../../shared/AsyncState';
import { api } from '../../api/client';
import './admin.css';

interface OrgMember {
  id: string;
  email: string;
  displayName: string;
  role: string;
  lastActiveAtUtc: string | null;
}

interface OrgDetails {
  id: string;
  name: string;
  slug: string;
  memberCount: number;
  createdAtUtc: string;
}

interface OrgAdminState {
  loading: boolean;
  error: string | null;
  org: OrgDetails | null;
  members: OrgMember[];
}

export function OrgAdminView() {
  const { org } = useAuth();
  const { isAdmin } = usePermissions();
  const mountedRef = useRef(true);
  useEffect(() => () => { mountedRef.current = false; }, []);

  const [state, setState] = useState<OrgAdminState>({
    loading: true,
    error: null,
    org: null,
    members: [],
  });

  const load = useCallback(async () => {
    setState(s => ({ ...s, loading: true, error: null }));
    try {
      const [tenants, policies] = await Promise.allSettled([
        api.listTenants() as Promise<OrgDetails[]>,
        api.listSecurityPolicies() as Promise<unknown[]>,
      ]);

      if (!mountedRef.current) return;

      const orgData = tenants.status === 'fulfilled' && tenants.value.length > 0
        ? tenants.value[0]
        : org
          ? { id: org.id, name: org.name, slug: org.slug, memberCount: 0, createdAtUtc: new Date().toISOString() }
          : null;

      // Derive members from policies response or use empty
      const policyData = policies.status === 'fulfilled' ? policies.value : [];

      setState({
        loading: false,
        error: null,
        org: orgData,
        members: policyData as OrgMember[],
      });
    } catch (err: unknown) {
      if (!mountedRef.current) return;
      const msg = err instanceof Error ? err.message : String(err);
      setState(s => ({ ...s, loading: false, error: msg }));
    }
  }, [org]);

  useEffect(() => {
    let cancelled = false;
    queueMicrotask(() => {
      load().then(() => { if (cancelled) return; });
    });
    return () => { cancelled = true; };
  }, [load]);

  return (
    <div className="page-shell admin-page">
      <header className="admin-page-header">
        <div>
          <h1 className="admin-page-title">Organization</h1>
          <p className="admin-page-subtitle">Manage your organization, members, and roles</p>
        </div>
      </header>

      <AsyncBoundary loading={state.loading} error={state.error} onRetry={load}>
        <div className="admin-grid">
          {/* Org Info Card */}
          <section className="admin-card">
            <h2 className="admin-card-title">Organization Details</h2>
            {state.org ? (
              <div className="admin-details">
                <div className="admin-detail-row">
                  <span className="admin-detail-label">Name</span>
                  <span className="admin-detail-value">{state.org.name}</span>
                </div>
                <div className="admin-detail-row">
                  <span className="admin-detail-label">Slug</span>
                  <span className="admin-detail-value admin-detail-mono">{state.org.slug}</span>
                </div>
                <div className="admin-detail-row">
                  <span className="admin-detail-label">ID</span>
                  <span className="admin-detail-value admin-detail-mono">{state.org.id}</span>
                </div>
              </div>
            ) : (
              <EmptyState title="No organization data" />
            )}
          </section>

          {/* Roles & Permissions Card */}
          <section className="admin-card">
            <h2 className="admin-card-title">Roles & Permissions</h2>
            <div className="admin-roles-grid">
              {(['Admin', 'Operator', 'Viewer'] as const).map(role => (
                <div key={role} className="admin-role-card">
                  <div className="admin-role-name">{role}</div>
                  <div className="admin-role-desc">
                    {role === 'Admin' && 'Full access to all resources and settings'}
                    {role === 'Operator' && 'Execute workflows, read policies, monitor system'}
                    {role === 'Viewer' && 'Read-only access to all operational data'}
                  </div>
                </div>
              ))}
            </div>
          </section>

          {/* Members Card */}
          <section className="admin-card admin-card--wide">
            <div className="admin-card-header">
              <h2 className="admin-card-title">Members</h2>
              {isAdmin && (
                <button className="exec-btn exec-btn--secondary" disabled title="Invite via API">
                  Invite Member
                </button>
              )}
            </div>
            {state.members.length > 0 ? (
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Role</th>
                    <th>Last Active</th>
                  </tr>
                </thead>
                <tbody>
                  {state.members.map(m => (
                    <tr key={m.id}>
                      <td>{m.displayName}</td>
                      <td className="admin-detail-mono">{m.email}</td>
                      <td><span className={`admin-role-badge admin-role-badge--${m.role.toLowerCase()}`}>{m.role}</span></td>
                      <td className="admin-detail-faint">{m.lastActiveAtUtc ? new Date(m.lastActiveAtUtc).toLocaleDateString() : '--'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <EmptyState
                title="No members loaded"
                description="Member listing requires the admin API endpoint"
              />
            )}
          </section>
        </div>
      </AsyncBoundary>
    </div>
  );
}
