import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, ProtectedRoute, LoginPage } from './auth';
import { AuthApiWiring } from './auth/AuthApiWiring';
import { AppShell } from './shell/AppShell';
import { CommandConsole } from './features/command';
import { StrategyView } from './features/strategy';
import { SystemActivityView } from './features/activity';
import { ControlPanel } from './features/control';
import { AuditLogView } from './features/audit';
import { ImpactDashboard } from './features/impact';
import { AttributionView } from './features/attribution';
import { OnboardingWizard } from './features/onboarding';
import { IntegrationMarketplace } from './features/integrations';
import { HumanOverridesView } from './features/overrides';
import { ExplanationView } from './features/explanations';
import { DecisionsView } from './features/decisions';
import { TrustTiersView } from './features/trust-tiers';
import { OrgAdminView } from './features/admin/OrgAdminView';
import { SystemHealthView } from './features/admin/SystemHealthView';
import { PermissionGate, UnauthorizedPage } from './shared/PermissionGate';

function Protected({ children }: { children: React.ReactNode }) {
  return <ProtectedRoute><AppShell>{children}</AppShell></ProtectedRoute>;
}

function AdminGated({ permission, children }: { permission: string; children: React.ReactNode }) {
  return (
    <Protected>
      <PermissionGate permission={permission} fallback={<UnauthorizedPage />}>
        {children}
      </PermissionGate>
    </Protected>
  );
}

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <AuthApiWiring />
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<Protected><CommandConsole /></Protected>} />
          <Route path="/onboarding" element={<Protected><OnboardingWizard /></Protected>} />
          <Route path="/integrations" element={<Protected><IntegrationMarketplace /></Protected>} />
          <Route path="/strategy/:goalId" element={<Protected><StrategyView /></Protected>} />
          <Route path="/attribution/:goalId" element={<Protected><AttributionView /></Protected>} />
          <Route path="/activity" element={<Protected><SystemActivityView /></Protected>} />
          <Route path="/control" element={<AdminGated permission="policy:read"><ControlPanel /></AdminGated>} />
          <Route path="/audit" element={<AdminGated permission="monitoring:read"><AuditLogView /></AdminGated>} />
          <Route path="/impact" element={<Protected><ImpactDashboard /></Protected>} />
          <Route path="/overrides" element={<AdminGated permission="governance:read"><HumanOverridesView /></AdminGated>} />
          <Route path="/explanations" element={<Protected><ExplanationView /></Protected>} />
          <Route path="/decisions" element={<Protected><DecisionsView /></Protected>} />
          <Route path="/trust-tiers" element={<AdminGated permission="governance:read"><TrustTiersView /></AdminGated>} />
          <Route path="/admin/org" element={<AdminGated permission="admin:read"><OrgAdminView /></AdminGated>} />
          <Route path="/admin/health" element={<AdminGated permission="monitoring:read"><SystemHealthView /></AdminGated>} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
