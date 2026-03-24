import type { ConnectionStatus } from '../types';

interface Props {
  status: ConnectionStatus;
  lastUpdated: string | null;
}

export function ConnectionBadge({ status, lastUpdated }: Props) {
  const label: Record<ConnectionStatus, string> = {
    connecting: 'Connecting',
    connected: 'Live',
    reconnecting: 'Reconnecting',
    disconnected: 'Offline',
  };

  const ago = lastUpdated ? formatAgo(lastUpdated) : null;

  return (
    <div className="sa-conn-badge" data-status={status}>
      <span className="sa-conn-dot" />
      <span className="sa-conn-label">{label[status]}</span>
      {ago && <span className="sa-conn-ago">{ago}</span>}
    </div>
  );
}

function formatAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  if (diff < 5000) return 'just now';
  if (diff < 60000) return `${Math.floor(diff / 1000)}s ago`;
  return `${Math.floor(diff / 60000)}m ago`;
}
