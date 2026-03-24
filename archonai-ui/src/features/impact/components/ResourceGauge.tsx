import type { SystemHealthDashboard } from '../types';

interface Props {
  health: SystemHealthDashboard;
}

function gaugeColor(pct: number): string {
  if (pct >= 90) return '#ef4444';
  if (pct >= 70) return '#f97316';
  if (pct >= 50) return '#facc15';
  return '#4ade80';
}

function GaugeMini({ label, value }: { label: string; value: number }) {
  const color = gaugeColor(value);
  const circumference = 2 * Math.PI * 18;
  const dashLen = (value / 100) * circumference;

  return (
    <div className="im-gauge">
      <svg viewBox="0 0 44 44" width="56" height="56">
        <circle cx="22" cy="22" r="18" fill="none" stroke="#27272a" strokeWidth="4" />
        <circle
          cx="22" cy="22" r="18"
          fill="none"
          stroke={color}
          strokeWidth="4"
          strokeLinecap="round"
          strokeDasharray={`${dashLen} ${circumference}`}
          transform="rotate(-90 22 22)"
          style={{ filter: `drop-shadow(0 0 3px ${color}40)` }}
        />
      </svg>
      <div className="im-gauge-inner">
        <span className="im-gauge-val" style={{ color }}>{Math.round(value)}%</span>
        <span className="im-gauge-label">{label}</span>
      </div>
    </div>
  );
}

export function ResourceGauge({ health }: Props) {
  const { resources } = health;

  return (
    <div className="im-resource">
      <span className="im-resource-title">Resource Utilization</span>
      <div className="im-gauges-row">
        <GaugeMini label="CPU" value={resources.cpuPercent} />
        <GaugeMini label="Memory" value={resources.memoryPercent} />
        <GaugeMini
          label="Disk"
          value={resources.diskTotalBytes > 0
            ? (resources.diskUsedBytes / resources.diskTotalBytes) * 100
            : 0}
        />
      </div>
      <div className="im-resource-stats">
        <span className="im-res-stat">
          <strong>{resources.activeThreads}</strong> threads
        </span>
        <span className="im-res-stat">
          <strong>{resources.pendingWorkItems}</strong> pending
        </span>
      </div>
    </div>
  );
}
