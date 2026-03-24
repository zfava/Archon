import type { ImpactMetric } from '../types';

interface Props {
  metric: ImpactMetric;
}

function formatValue(value: number, unit: string): string {
  if (unit === '$') {
    if (value >= 1000000) return `$${(value / 1000000).toFixed(1)}M`;
    if (value >= 1000) return `$${(value / 1000).toFixed(1)}K`;
    return `$${value.toFixed(2)}`;
  }
  if (unit === '%') return `${value.toFixed(1)}%`;
  if (unit === 'hrs') {
    if (value >= 1000) return `${(value / 1000).toFixed(1)}K`;
    return value.toFixed(1);
  }
  if (value >= 1000000) return `${(value / 1000000).toFixed(1)}M`;
  if (value >= 1000) return `${(value / 1000).toFixed(1)}K`;
  return String(Math.round(value));
}

function categoryColor(category: ImpactMetric['category']): string {
  switch (category) {
    case 'revenue': return '#4ade80';
    case 'cost': return '#6366f1';
    case 'efficiency': return '#06b6d4';
  }
}

export function ImpactMetricCard({ metric }: Props) {
  const color = categoryColor(metric.category);
  const isPositive = metric.trend >= 0;

  return (
    <div className="im-metric-card">
      <div className="im-metric-header">
        <span className="im-metric-label">{metric.label}</span>
        <span className="im-metric-cat" style={{ color }}>{metric.category}</span>
      </div>
      <div className="im-metric-value" style={{ color }}>
        {formatValue(metric.value, metric.unit)}
        {metric.unit === 'hrs' && <span className="im-metric-unit">hrs</span>}
      </div>
      <div className="im-metric-trend">
        <span className={`im-trend-badge ${isPositive ? 'im-trend--up' : 'im-trend--down'}`}>
          <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3">
            {isPositive
              ? <path d="M18 15l-6-6-6 6" />
              : <path d="M6 9l6 6 6-6" />}
          </svg>
          {Math.abs(metric.trend).toFixed(1)}%
        </span>
        <span className="im-trend-label">{metric.trendLabel}</span>
      </div>
    </div>
  );
}
