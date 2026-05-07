import type { BeforeAfterMetric } from '../types';

interface Props {
  metrics: BeforeAfterMetric[];
}

function formatVal(v: number, unit: string): string {
  if (unit === '$') return `$${v.toFixed(2)}`;
  if (unit === '%') return `${v.toFixed(1)}%`;
  if (unit === 's') return `${v.toFixed(1)}s`;
  return String(Math.round(v));
}

export function BeforeAfterChart({ metrics }: Props) {
  if (metrics.length === 0) return null;

  return (
    <div className="im-ba-chart">
      <div className="im-ba-legend">
        <span className="im-ba-legend-item">
          <span className="im-ba-dot im-ba-dot--before" />
          Before ArchonAI
        </span>
        <span className="im-ba-legend-item">
          <span className="im-ba-dot im-ba-dot--after" />
          With ArchonAI
        </span>
      </div>
      <div className="im-ba-rows">
        {metrics.map((m) => {
          const max = Math.max(m.before, m.after, 1);
          const beforePct = (m.before / max) * 100;
          const afterPct = (m.after / max) * 100;
          const isImproved = m.improvement > 0;

          return (
            <div key={m.label} className="im-ba-row">
              <div className="im-ba-row-header">
                <span className="im-ba-row-label">{m.label}</span>
                <span
                  className={`im-ba-change ${isImproved ? 'im-ba-change--pos' : 'im-ba-change--neg'}`}
                >
                  {isImproved ? '+' : ''}{m.improvement.toFixed(1)}%
                </span>
              </div>
              <div className="im-ba-bars">
                <div className="im-ba-bar-row">
                  <div className="im-ba-bar im-ba-bar--before" style={{ width: `${beforePct}%` }} />
                  <span className="im-ba-bar-val">{formatVal(m.before, m.unit)}</span>
                </div>
                <div className="im-ba-bar-row">
                  <div className="im-ba-bar im-ba-bar--after" style={{ width: `${afterPct}%` }} />
                  <span className="im-ba-bar-val">{formatVal(m.after, m.unit)}</span>
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
