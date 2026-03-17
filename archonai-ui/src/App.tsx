import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, ProtectedRoute, LoginPage } from './auth';
import { AuthApiWiring } from './auth/AuthApiWiring';
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

function Protected({ children }: { children: React.ReactNode }) {
  return <ProtectedRoute>{children}</ProtectedRoute>;
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
          <Route path="/control" element={<Protected><ControlPanel /></Protected>} />
          <Route path="/audit" element={<Protected><AuditLogView /></Protected>} />
          <Route path="/impact" element={<Protected><ImpactDashboard /></Protected>} />
          <Route path="/overrides" element={<Protected><HumanOverridesView /></Protected>} />
          <Route path="/explanations" element={<Protected><ExplanationView /></Protected>} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
