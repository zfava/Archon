import { type ReactNode } from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { usePermissions } from '../auth/usePermissions';
import './app-shell.css';

interface NavItem {
  to: string;
  label: string;
  icon: string;
  permission?: string;
  section: 'operations' | 'admin';
}

const NAV_ITEMS: NavItem[] = [
  { to: '/executive', label: 'Executive', icon: 'briefcase', section: 'operations', permission: 'governance:read' },
  { to: '/', label: 'Command', icon: 'terminal', section: 'operations' },
  { to: '/activity', label: 'Activity', icon: 'activity', section: 'operations' },
  { to: '/decisions', label: 'Decisions', icon: 'zap', section: 'operations' },
  { to: '/impact', label: 'Impact', icon: 'bar-chart', section: 'operations' },
  { to: '/control', label: 'Control', icon: 'settings', section: 'operations', permission: 'policy:read' },
  { to: '/integrations', label: 'Integrations', icon: 'plug', section: 'operations', permission: 'connectors:read' },
  { to: '/audit', label: 'Audit Log', icon: 'file-text', section: 'admin', permission: 'monitoring:read' },
  { to: '/trust-tiers', label: 'Trust Tiers', icon: 'layers', section: 'admin', permission: 'governance:read' },
  { to: '/memory', label: 'Memory', icon: 'database', section: 'admin', permission: 'governance:read' },
  { to: '/operational-twin', label: 'Op Twin', icon: 'git-merge', section: 'admin', permission: 'governance:read' },
  { to: '/proof-analytics', label: 'Proof', icon: 'check-circle', section: 'operations', permission: 'governance:read' },
  { to: '/action-safety', label: 'Safety', icon: 'shield-off', section: 'operations', permission: 'governance:read' },
  { to: '/hero-workflows', label: 'Workflows', icon: 'play', section: 'operations', permission: 'governance:read' },
  { to: '/simulation', label: 'Simulation', icon: 'eye', section: 'operations', permission: 'governance:read' },
  { to: '/scenarios', label: 'Scenarios', icon: 'compass', section: 'operations', permission: 'governance:read' },
  { to: '/exceptions', label: 'Exceptions', icon: 'alert-triangle', section: 'operations', permission: 'governance:read' },
  { to: '/overrides', label: 'Overrides', icon: 'shield', section: 'admin', permission: 'governance:read' },
  { to: '/admin/org', label: 'Organization', icon: 'users', section: 'admin', permission: 'admin:read' },
  { to: '/admin/health', label: 'System Health', icon: 'heart', section: 'admin', permission: 'monitoring:read' },
];

const ICONS: Record<string, ReactNode> = {
  terminal: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/></svg>,
  activity: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M22 12h-4l-3 9L9 3l-3 9H2"/></svg>,
  zap: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>,
  'bar-chart': <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/></svg>,
  settings: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 01-2.83 2.83l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z"/></svg>,
  plug: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 2v6m0 8v6m-5-15l3 3m4 4l3 3m-10 0l3-3m4-4l3-3"/></svg>,
  'file-text': <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>,
  layers: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/></svg>,
  database: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/></svg>,
  shield: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>,
  users: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 00-3-3.87"/><path d="M16 3.13a4 4 0 010 7.75"/></svg>,
  heart: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M20.84 4.61a5.5 5.5 0 00-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 00-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 000-7.78z"/></svg>,
  'git-merge': <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="18" cy="18" r="3"/><circle cx="6" cy="6" r="3"/><path d="M6 21V9a9 9 0 009 9"/></svg>,
  play: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polygon points="5 3 19 12 5 21 5 3"/></svg>,
  eye: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>,
  compass: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="10"/><polygon points="16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76"/></svg>,
  'alert-triangle': <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>,
  'check-circle': <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>,
  'shield-off': <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9.5 9l5 5m0-5l-5 5" /></svg>,
  briefcase: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="2" y="7" width="20" height="14" rx="2" ry="2"/><path d="M16 21V5a2 2 0 00-2-2h-4a2 2 0 00-2 2v16"/></svg>,
  logout: <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>,
};

export function AppShell({ children }: { children: ReactNode }) {
  const { user, org, logout } = useAuth();
  const { hasPermission } = usePermissions();
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

  const visibleItems = NAV_ITEMS.filter(
    (item) => !item.permission || hasPermission(item.permission),
  );

  const opsItems = visibleItems.filter((i) => i.section === 'operations');
  const adminItems = visibleItems.filter((i) => i.section === 'admin');

  return (
    <div className="app-shell">
      <aside className="app-sidebar">
        <div className="app-sidebar-brand">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M12 2L2 7l10 5 10-5-10-5z" />
            <path d="M2 17l10 5 10-5" />
            <path d="M2 12l10 5 10-5" />
          </svg>
          <span>ArchonAI</span>
        </div>

        {org && (
          <div className="app-sidebar-tenant">
            <span className="app-sidebar-tenant-label">Tenant</span>
            <span className="app-sidebar-tenant-name">{org.name}</span>
          </div>
        )}

        <nav className="app-sidebar-nav">
          <div className="app-sidebar-section-label">Operations</div>
          {opsItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              className={({ isActive }) =>
                `app-sidebar-link ${isActive ? 'app-sidebar-link--active' : ''}`
              }
            >
              {ICONS[item.icon]}
              <span>{item.label}</span>
            </NavLink>
          ))}

          {adminItems.length > 0 && (
            <>
              <div className="app-sidebar-section-label">Administration</div>
              {adminItems.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  className={({ isActive }) =>
                    `app-sidebar-link ${isActive ? 'app-sidebar-link--active' : ''}`
                  }
                >
                  {ICONS[item.icon]}
                  <span>{item.label}</span>
                </NavLink>
              ))}
            </>
          )}
        </nav>

        <div className="app-sidebar-footer">
          {user && (
            <div className="app-sidebar-user">
              <div className="app-sidebar-user-avatar">
                {user.displayName.charAt(0).toUpperCase()}
              </div>
              <div className="app-sidebar-user-info">
                <span className="app-sidebar-user-name">{user.displayName}</span>
                <span className="app-sidebar-user-role">{user.role}</span>
              </div>
            </div>
          )}
          <button className="app-sidebar-logout" onClick={handleLogout} title="Sign out">
            {ICONS.logout}
          </button>
        </div>
      </aside>

      <main className="app-main">
        {children}
      </main>
    </div>
  );
}
