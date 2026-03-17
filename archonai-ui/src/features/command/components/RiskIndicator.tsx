interface Props {
  score: number;
  level: string;
  size?: 'sm' | 'md';
}

function riskColor(score: number): string {
  if (score >= 0.7) return '#ef4444';
  if (score >= 0.4) return '#f97316';
  if (score >= 0.2) return '#facc15';
  return '#4ade80';
}

function riskBg(score: number): string {
  if (score >= 0.7) return '#3b1111';
  if (score >= 0.4) return '#2e1a0a';
  if (score >= 0.2) return '#2e2a1a';
  return '#052e16';
}

export function RiskIndicator({ score, level, size = 'md' }: Props) {
  const pct = Math.round(score * 100);
  const color = riskColor(score);
  const bg = riskBg(score);
  const isSm = size === 'sm';

  return (
    <div className={`risk-indicator ${isSm ? 'risk-indicator--sm' : ''}`}>
      {/* Gauge arc */}
      <svg
        viewBox="0 0 60 36"
        className="risk-gauge"
        width={isSm ? 48 : 64}
        height={isSm ? 28 : 38}
      >
        {/* Background arc */}
        <path
          d="M 6 32 A 24 24 0 0 1 54 32"
          fill="none"
          stroke="#27272a"
          strokeWidth="5"
          strokeLinecap="round"
        />
        {/* Filled arc — clipped to score */}
        <path
          d="M 6 32 A 24 24 0 0 1 54 32"
          fill="none"
          stroke={color}
          strokeWidth="5"
          strokeLinecap="round"
          strokeDasharray={`${pct * 0.75} 100`}
          style={{ filter: `drop-shadow(0 0 4px ${color}40)` }}
        />
      </svg>
      <div className="risk-label-group">
        <span className="risk-pct" style={{ color }}>{pct}%</span>
        <span className="risk-level-text" style={{ background: bg, color }}>
          {level}
        </span>
      </div>
    </div>
  );
}
