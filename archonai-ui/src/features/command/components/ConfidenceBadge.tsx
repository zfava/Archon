interface Props {
  value: number;
  label?: string;
}

export function ConfidenceBadge({ value, label }: Props) {
  const pct = Math.round(value * 100);
  const level = pct >= 80 ? 'high' : pct >= 50 ? 'medium' : 'low';

  return (
    <span className={`confidence-badge confidence-${level}`} title={label ?? 'Confidence'}>
      {pct}%
    </span>
  );
}
