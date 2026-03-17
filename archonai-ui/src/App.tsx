import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { CommandConsole } from './features/command';
import { StrategyView } from './features/strategy';
import { SystemActivityView } from './features/activity';
import { ControlPanel } from './features/control';
import { AuditLogView } from './features/audit';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<CommandConsole />} />
        <Route path="/strategy/:goalId" element={<StrategyView />} />
        <Route path="/activity" element={<SystemActivityView />} />
        <Route path="/control" element={<ControlPanel />} />
        <Route path="/audit" element={<AuditLogView />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
