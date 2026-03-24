import type { SystemConnection } from '../types';

const CATEGORY_LABELS: Record<string, string> = {
  crm: 'CRM',
  erp: 'Enterprise',
  messaging: 'Messaging',
  finance: 'Finance',
  productivity: 'Productivity',
  support: 'Support',
};

interface Props {
  systems: SystemConnection[];
  onToggle: (id: string) => void;
}

export function ConnectSystems({ systems, onToggle }: Props) {
  const categories = [...new Set(systems.map(s => s.category))];

  return (
    <div className="ob-step-content">
      <div className="ob-step-intro">
        <h2 className="ob-step-title">Connect your systems</h2>
        <p className="ob-step-desc">
          Select the platforms ArchonAI will integrate with. Agents will use these
          connections to read data, push results, and coordinate across your stack.
        </p>
      </div>

      {categories.map(cat => (
        <div key={cat} className="ob-system-group">
          <span className="ob-group-label">{CATEGORY_LABELS[cat] || cat}</span>
          <div className="ob-system-grid">
            {systems.filter(s => s.category === cat).map(sys => (
              <button
                key={sys.id}
                className={`ob-system-card ${sys.connected ? 'ob-system-card--active' : ''}`}
                onClick={() => onToggle(sys.id)}
                disabled={sys.configuring}
              >
                <div className="ob-system-status">
                  {sys.configuring ? (
                    <div className="phase-spinner" />
                  ) : sys.connected ? (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#4ade80" strokeWidth="2">
                      <path d="M20 6L9 17l-5-5" />
                    </svg>
                  ) : (
                    <div className="ob-system-dot" />
                  )}
                </div>
                <span className="ob-system-name">{sys.name}</span>
                <span className="ob-system-desc">{sys.description}</span>
              </button>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
