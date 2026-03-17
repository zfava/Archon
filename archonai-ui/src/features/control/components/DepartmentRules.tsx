import type { DepartmentApproval, DepartmentRule, ExecutionMode } from '../types';

interface Props {
  rules: DepartmentRule[];
  globalMode: ExecutionMode;
  onChange: (rule: DepartmentRule) => void;
}

const APPROVAL_OPTIONS: { value: DepartmentApproval; label: string }[] = [
  { value: 'inherit', label: 'Inherit global' },
  { value: 'require-approval', label: 'Require approval' },
  { value: 'auto-execute', label: 'Auto-execute' },
];

function effectiveLabel(rule: DepartmentRule, globalMode: ExecutionMode): string {
  if (rule.approval !== 'inherit') {
    return rule.approval === 'require-approval' ? 'Approval required' : 'Auto-execute';
  }
  switch (globalMode) {
    case 'observe': return 'Observe (inherited)';
    case 'recommend': return 'Recommend (inherited)';
    case 'execute': return 'Execute (inherited)';
  }
}

function effectiveClass(rule: DepartmentRule, globalMode: ExecutionMode): string {
  if (rule.approval === 'require-approval') return 'cp-eff--approval';
  if (rule.approval === 'auto-execute') return 'cp-eff--auto';
  if (globalMode === 'observe') return 'cp-eff--observe';
  if (globalMode === 'execute') return 'cp-eff--execute';
  return 'cp-eff--recommend';
}

export function DepartmentRules({ rules, globalMode, onChange }: Props) {
  return (
    <section className="cp-card">
      <span className="cp-section-label">Department Rules</span>
      <p className="cp-section-desc">
        Override execution behavior per department. Rules not set inherit the global mode.
      </p>

      <div className="cp-dept-list">
        {rules.map((rule) => (
          <div key={rule.department} className="cp-dept-row">
            <div className="cp-dept-header">
              <span className="cp-dept-name">{rule.department}</span>
              <span className={`cp-dept-effective ${effectiveClass(rule, globalMode)}`}>
                {effectiveLabel(rule, globalMode)}
              </span>
            </div>

            <div className="cp-dept-controls">
              {/* Approval mode */}
              <div className="cp-dept-field">
                <label className="cp-field-label">Approval</label>
                <select
                  className="cp-select"
                  value={rule.approval}
                  onChange={(e) =>
                    onChange({ ...rule, approval: e.target.value as DepartmentApproval })
                  }
                >
                  {APPROVAL_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </div>

              {/* Max auto cost */}
              <div className="cp-dept-field">
                <label className="cp-field-label">Max auto cost ($)</label>
                <input
                  type="number"
                  className="cp-input-num"
                  value={rule.maxAutoCost}
                  min={0}
                  step={50}
                  onChange={(e) =>
                    onChange({ ...rule, maxAutoCost: Number(e.target.value) || 0 })
                  }
                />
              </div>

              {/* Max auto risk */}
              <div className="cp-dept-field">
                <label className="cp-field-label">Max auto risk (%)</label>
                <input
                  type="number"
                  className="cp-input-num"
                  value={rule.maxAutoRisk}
                  min={0}
                  max={100}
                  step={5}
                  onChange={(e) =>
                    onChange({ ...rule, maxAutoRisk: Number(e.target.value) || 0 })
                  }
                />
              </div>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
