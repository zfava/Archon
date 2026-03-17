import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
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

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<CommandConsole />} />
        <Route path="/onboarding" element={<OnboardingWizard />} />
        <Route path="/integrations" element={<IntegrationMarketplace />} />
        <Route path="/strategy/:goalId" element={<StrategyView />} />
        <Route path="/attribution/:goalId" element={<AttributionView />} />
        <Route path="/activity" element={<SystemActivityView />} />
        <Route path="/control" element={<ControlPanel />} />
        <Route path="/audit" element={<AuditLogView />} />
        <Route path="/impact" element={<ImpactDashboard />} />
        <Route path="/overrides" element={<HumanOverridesView />} />
        <Route path="/explanations" element={<ExplanationView />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
