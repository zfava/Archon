interface Props {
  value: number;
  label?: string;
}

const RADIUS = 10;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

export function ConfidenceBadge({ value, label }: Props) {
  const pct = Math.round(value * 100);
  const level = pct >= 80 ? 'high' : pct >= 50 ? 'medium' : 'low';
  const offset = CIRCUMFERENCE - (value * CIRCUMFERENCE);

  return (
    <span className="confidence-badge-wrap" title={label ?? 'Confidence'}>
      <svg width="26" height="26" viewBox="0 0 26 26" className="confidence-arc">
        <circle
          className="confidence-arc-bg"
          cx="13"
          cy="13"
          r={RADIUS}
        />
        <circle
          className="confidence-arc-fill"
          data-level={level}
          cx="13"
          cy="13"
          r={RADIUS}
          strokeDasharray={CIRCUMFERENCE}
          strokeDashoffset={offset}
        />
      </svg>
      <span className="confidence-pct" data-level={level}>
        {pct}%
      </span>
    </span>
  );
}
