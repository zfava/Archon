import type { ExecutionMode } from '../types';
import { EXECUTION_MODES } from '../types';

interface Props {
  value: ExecutionMode;
  onChange: (mode: ExecutionMode) => void;
}

const MODE_ICONS: Record<ExecutionMode, string> = {
  observe: '\uD83D\uDC41',   // eye
  recommend: '\uD83D\uDCA1', // light bulb
  execute: '\u26A1',          // lightning
};

export function ExecutionModeSelector({ value, onChange }: Props) {
  return (
    <section className="cp-card">
      <span className="cp-section-label">Execution Mode</span>
      <p className="cp-section-desc">
        Set how much autonomy the system has when executing actions.
      </p>

      <div className="cp-mode-grid">
        {EXECUTION_MODES.map(({ mode, label, description, behaviors }) => (
          <button
            key={mode}
            className={`cp-mode-btn ${value === mode ? 'cp-mode-btn--active' : ''}`}
            onClick={() => onChange(mode)}
          >
            <span className="cp-mode-icon">{MODE_ICONS[mode]}</span>
            <span className="cp-mode-name">{label}</span>
            <span className="cp-mode-desc">{description}</span>

            {value === mode && (
              <ul className="cp-mode-behaviors">
                {behaviors.map((b, i) => (
                  <li key={i} className="cp-mode-behavior">{b}</li>
                ))}
              </ul>
            )}
          </button>
        ))}
      </div>
    </section>
  );
}
