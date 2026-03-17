import type { TaskPerformanceTrend, ModelUsageTrend } from '../types';

type Trend = TaskPerformanceTrend | ModelUsageTrend;

interface Props {
  trends: Trend[];
  title: string;
}

function trendColor(direction: string): string {
  switch (direction.toLowerCase()) {
    case 'improving': return '#4ade80';
    case 'declining': return '#ef4444';
    case 'stable': return '#71717a';
    default: return '#a1a1aa';
  }
}

function trendIcon(direction: string): string {
  switch (direction.toLowerCase()) {
    case 'improving': return '\u2197';
    case 'declining': return '\u2198';
    default: return '\u2192';
  }
}

export function TrendSpark({ trends, title }: Props) {
  if (trends.length === 0) return null;

  return (
    <div className="im-trends">
      <span className="im-trends-title">{title}</span>
      <div className="im-trends-list">
        {trends.slice(0, 6).map((t, i) => {
          const color = trendColor(t.trendDirection);
          return (
            <div key={i} className="im-trend-item">
              <span className="im-trend-metric">{t.metricName}</span>
              <div className="im-trend-data">
                <span className="im-trend-icon" style={{ color }}>
                  {trendIcon(t.trendDirection)}
                </span>
                <span className="im-trend-pct" style={{ color }}>
                  {t.changePercent >= 0 ? '+' : ''}{t.changePercent.toFixed(1)}%
                </span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
