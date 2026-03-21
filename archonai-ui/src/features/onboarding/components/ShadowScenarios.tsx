import type { ShadowScenario } from '../types';

interface Props {
  scenarios: ShadowScenario[];
}

export function ShadowScenarios({ scenarios }: Props) {
  if (scenarios.length === 0) return null;

  return (
    <div className="ob-shadow-section">
      <span className="ob-group-label">What ArchonAI Would Have Caught</span>
      <div className="ob-shadow-list">
        {scenarios.map(s => (
          <div key={s.title} className="ob-shadow-card">
            <div className="ob-shadow-header">
              <span className="ob-shadow-title">{s.title}</span>
              <span className="ob-shadow-savings">{s.savings}</span>
            </div>
            <div className="ob-shadow-row">
              <span className="ob-shadow-label">Problem</span>
              <span className="ob-shadow-text">{s.problem}</span>
            </div>
            <div className="ob-shadow-row">
              <span className="ob-shadow-label">Detection</span>
              <span className="ob-shadow-text">{s.detection}</span>
            </div>
            <div className="ob-shadow-row">
              <span className="ob-shadow-label">Action</span>
              <span className="ob-shadow-text">{s.action}</span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
