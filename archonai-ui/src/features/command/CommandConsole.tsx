import { useRef, useEffect, useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { useCommand } from './hooks/useCommand';
import { CommandInput } from './components/CommandInput';
import { CommandResponse } from './components/CommandResponse';
import { ExecutionControls } from './components/ExecutionControls';
import { PhaseIndicator } from './components/PhaseIndicator';
import './command.css';

export function CommandConsole() {
  const { history, current, phase, submit, execute, simulate, cancel } = useCommand();
  const scrollRef = useRef<HTMLDivElement>(null);
  const [showSimDialog, setShowSimDialog] = useState(false);
  const [simStrategies, setSimStrategies] = useState('balanced, safe-mode, throughput');

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [history, phase]);

  const handleModify = useCallback(() => {
    // Focus the input — the user can re-enter a refined command
    const input = document.querySelector<HTMLTextAreaElement>('.cmd-input');
    input?.focus();
  }, []);

  const handleSimulate = useCallback(() => {
    setShowSimDialog(true);
  }, []);

  const handleSimSubmit = useCallback(() => {
    const strategies = simStrategies
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    if (strategies.length > 0) {
      simulate(strategies);
    }
    setShowSimDialog(false);
  }, [simStrategies, simulate]);

  const isInputDisabled = ['parsing', 'planning', 'simulating', 'executing'].includes(phase);

  return (
    <div className="cmd-console">
      {/* Header */}
      <header className="cmd-header">
        <div className="cmd-brand">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M12 2L2 7l10 5 10-5-10-5z" />
            <path d="M2 17l10 5 10-5" />
            <path d="M2 12l10 5 10-5" />
          </svg>
          <span>ArchonAI</span>
        </div>
        <div className="cmd-header-right">
          <Link to="/activity" className="cmd-nav-link" title="System Activity">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M22 12h-4l-3 9L9 3l-3 9H2" />
            </svg>
            Activity
          </Link>
          <PhaseIndicator phase={phase} />
        </div>
      </header>

      {/* Response area */}
      <div className="cmd-scroll" ref={scrollRef}>
        {history.length === 0 && (
          <div className="cmd-empty">
            <h2>ArchonAI Command Interface</h2>
            <p>Enter a natural language command to plan, simulate, and execute operations.</p>
            <div className="cmd-examples">
              <button onClick={() => submit('Optimize supply chain for Q2 demand surge')}>
                Optimize supply chain for Q2 demand surge
              </button>
              <button onClick={() => submit('Reduce customer churn in enterprise segment')}>
                Reduce customer churn in enterprise segment
              </button>
              <button onClick={() => submit('Scale marketing budget allocation using cost-optimized strategy')}>
                Scale marketing budget using cost-optimized
              </button>
            </div>
          </div>
        )}

        {history.map((entry) => (
          <div key={entry.id} className="cmd-entry">
            <div className="cmd-user-msg">
              <span className="cmd-chevron">&#9656;</span>
              <span>{entry.input}</span>
            </div>
            <CommandResponse result={entry} />
            {entry.id === current?.id && (
              <ExecutionControls
                phase={entry.phase}
                onExecute={execute}
                onModify={handleModify}
                onSimulate={handleSimulate}
                onCancel={cancel}
              />
            )}
          </div>
        ))}
      </div>

      {/* Simulate dialog */}
      {showSimDialog && (
        <div className="sim-dialog-overlay" onClick={() => setShowSimDialog(false)}>
          <div className="sim-dialog" onClick={(e) => e.stopPropagation()}>
            <h3>Simulate Strategies</h3>
            <p>Enter comma-separated strategies to compare.</p>
            <input
              className="sim-input"
              value={simStrategies}
              onChange={(e) => setSimStrategies(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSimSubmit()}
              autoFocus
            />
            <div className="sim-dialog-actions">
              <button className="exec-btn exec-btn--secondary" onClick={() => setShowSimDialog(false)}>
                Cancel
              </button>
              <button className="exec-btn exec-btn--primary" onClick={handleSimSubmit}>
                Run Simulation
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Input */}
      <CommandInput
        onSubmit={submit}
        disabled={isInputDisabled}
        placeholder="What should ArchonAI do?"
      />
    </div>
  );
}
