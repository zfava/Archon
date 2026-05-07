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
          <Link to="/control" className="cmd-nav-link" title="Control Panel">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="12" cy="12" r="3" />
              <path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 01-2.83 2.83l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z" />
            </svg>
            Control
          </Link>
          <Link to="/audit" className="cmd-nav-link" title="Audit Log">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
              <polyline points="14 2 14 8 20 8" />
              <line x1="16" y1="13" x2="8" y2="13" />
              <line x1="16" y1="17" x2="8" y2="17" />
              <polyline points="10 9 9 9 8 9" />
            </svg>
            Audit
          </Link>
          <Link to="/impact" className="cmd-nav-link" title="Business Impact">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <line x1="18" y1="20" x2="18" y2="10" />
              <line x1="12" y1="20" x2="12" y2="4" />
              <line x1="6" y1="20" x2="6" y2="14" />
            </svg>
            Impact
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
