import type { CommandPhase } from '../types';

interface Props {
  phase: CommandPhase;
  onExecute: () => void;
  onModify: () => void;
  onSimulate: () => void;
  onCancel: () => void;
}

export function ExecutionControls({
  phase,
  onExecute,
  onModify,
  onSimulate,
  onCancel,
}: Props) {
  if (phase === 'idle' || phase === 'parsing') return null;

  const isLoading = ['planning', 'simulating', 'executing'].includes(phase);
  const isReady = phase === 'ready';
  const isDone = phase === 'complete';

  return (
    <div className="exec-controls">
      {isReady && (
        <>
          <button className="exec-btn exec-btn--primary" onClick={onExecute}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
              <polygon points="5,3 19,12 5,21" />
            </svg>
            Execute
          </button>
          <button className="exec-btn exec-btn--secondary" onClick={onModify}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" />
              <path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" />
            </svg>
            Modify
          </button>
          <button className="exec-btn exec-btn--secondary" onClick={onSimulate}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="12" cy="12" r="10" />
              <path d="M12 6v6l4 2" />
            </svg>
            Simulate
          </button>
        </>
      )}

      {isLoading && (
        <button className="exec-btn exec-btn--danger" onClick={onCancel}>
          Cancel
        </button>
      )}

      {isDone && (
        <span className="exec-done">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <polyline points="20 6 9 17 4 12" />
          </svg>
          Execution complete
        </span>
      )}

      {phase === 'error' && (
        <button className="exec-btn exec-btn--secondary" onClick={onCancel}>
          Dismiss
        </button>
      )}
    </div>
  );
}
