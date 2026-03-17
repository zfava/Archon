import type { AutomationConfig, AutomationLevel } from '../types';

const LEVELS: { id: AutomationLevel; label: string; desc: string }[] = [
  { id: 'approval', label: 'Approval Required', desc: 'Every action needs human sign-off before execution.' },
  { id: 'assisted', label: 'Assisted', desc: 'Agents recommend actions; humans approve high-impact ones.' },
  { id: 'autonomous', label: 'Fully Autonomous', desc: 'Agents execute within policy guardrails without waiting for approval.' },
];

interface Props {
  config: AutomationConfig;
  onSetLevel: (level: AutomationLevel) => void;
  onSetDeptLevel: (name: string, level: AutomationLevel) => void;
  onToggleDept: (name: string) => void;
}

export function ConfigureAutomation({ config, onSetLevel, onSetDeptLevel, onToggleDept }: Props) {
  return (
    <div className="ob-step-content">
      <div className="ob-step-intro">
        <h2 className="ob-step-title">Configure automation level</h2>
        <p className="ob-step-desc">
          Choose how much autonomy ArchonAI agents should have. You can override per department.
        </p>
      </div>

      {/* Global level */}
      <div className="ob-auto-section">
        <span className="ob-group-label">Global Default</span>
        <div className="ob-level-grid">
          {LEVELS.map(l => (
            <button
              key={l.id}
              className={`ob-level-card ${config.level === l.id ? 'ob-level-card--active' : ''}`}
              onClick={() => onSetLevel(l.id)}
            >
              <span className="ob-level-name">{l.label}</span>
              <span className="ob-level-desc">{l.desc}</span>
            </button>
          ))}
        </div>
      </div>

      {/* Per-department overrides */}
      <div className="ob-auto-section">
        <span className="ob-group-label">Department Overrides</span>
        <div className="ob-dept-list">
          {config.departments.map(dept => (
            <div key={dept.name} className={`ob-dept-row ${!dept.enabled ? 'ob-dept-row--disabled' : ''}`}>
              <label className="ob-dept-toggle">
                <input
                  type="checkbox"
                  checked={dept.enabled}
                  onChange={() => onToggleDept(dept.name)}
                />
                <span className="ob-dept-name">{dept.name}</span>
              </label>
              {dept.enabled && (
                <select
                  className="ob-dept-select"
                  value={dept.level}
                  onChange={e => onSetDeptLevel(dept.name, e.target.value as AutomationLevel)}
                >
                  {LEVELS.map(l => (
                    <option key={l.id} value={l.id}>{l.label}</option>
                  ))}
                </select>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
