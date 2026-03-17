import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { CommandConsole } from './features/command';
import { StrategyView } from './features/strategy';

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<CommandConsole />} />
        <Route path="/strategy/:goalId" element={<StrategyView />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
