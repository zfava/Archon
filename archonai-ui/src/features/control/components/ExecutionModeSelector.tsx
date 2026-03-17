import type { ExecutionMode } from '../types';
import { EXECUTION_MODES } from '../types';

interface Props {
  value: ExecutionMode;
  onChange: (mode: ExecutionMode) => void;
}

const MODE_ICONS: Record<ExecutionMode, string> = {
  manual: '\u270B',     // raised hand
  assisted: '\u2696',   // scales
  autonomous: '\u26A1', // lightning
};

export function ExecutionModeSelector({ value, onChange }: Props) {
  return (
    <section className="cp-card">
      <span className="cp-section-label">Execution Mode</span>
      <p className="cp-section-desc">
        Set how much autonomy the system has when executing actions.
      </p>

      <div className="cp-mode-grid">
        {EXECUTION_MODES.map(({ mode, description }) => (
          <button
            key={mode}
            className={`cp-mode-btn ${value === mode ? 'cp-mode-btn--active' : ''}`}
            onClick={() => onChange(mode)}
          >
            <span className="cp-mode-icon">{MODE_ICONS[mode]}</span>
            <span className="cp-mode-name">{mode}</span>
            <span className="cp-mode-desc">{description}</span>
          </button>
        ))}
      </div>
    </section>
  );
}
