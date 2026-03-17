import type { ConnectorInfo } from '../types';

const STATUS_CONFIG: Record<string, { label: string; className: string }> = {
  connected: { label: 'Connected', className: 'im-status--connected' },
  connecting: { label: 'Connecting...', className: 'im-status--connecting' },
  disconnected: { label: 'Not connected', className: 'im-status--disconnected' },
  error: { label: 'Error', className: 'im-status--error' },
};

interface Props {
  connector: ConnectorInfo;
  onConnect: (id: string) => void;
  onDisconnect: (id: string) => void;
}

export function ConnectorCard({ connector, onConnect, onDisconnect }: Props) {
  const cfg = STATUS_CONFIG[connector.status] ?? STATUS_CONFIG.disconnected;
  const isConnected = connector.status === 'connected';
  const isLoading = connector.status === 'connecting';

  return (
    <div className={`im-card ${isConnected ? 'im-card--connected' : ''}`}>
      <div className="im-card-header">
        <div className="im-card-title-row">
          <span className="im-card-name">{connector.name}</span>
          <span className="im-card-category">{connector.category.toUpperCase()}</span>
        </div>
        <span className={`im-status-badge ${cfg.className}`}>{cfg.label}</span>
      </div>

      <p className="im-card-desc">{connector.description}</p>

      <div className="im-card-features">
        {connector.features.map(f => (
          <span key={f} className="im-feature-chip">{f}</span>
        ))}
      </div>

      {/* Stats row for connected connectors */}
      {isConnected && (
        <div className="im-card-stats">
          <div className="im-stat">
            <span className="im-stat-val">{connector.totalRequests.toLocaleString()}</span>
            <span className="im-stat-label">Requests</span>
          </div>
          <div className="im-stat">
            <span className={`im-stat-val ${connector.failedRequests > 0 ? 'im-stat-val--warn' : ''}`}>
              {connector.failedRequests}
            </span>
            <span className="im-stat-label">Failed</span>
          </div>
          {connector.rateLimitRemaining !== null && (
            <div className="im-stat">
              <span className="im-stat-val">{connector.rateLimitRemaining}</span>
              <span className="im-stat-label">Rate limit</span>
            </div>
          )}
          {connector.lastSyncedAt && (
            <div className="im-stat">
              <span className="im-stat-val im-stat-val--time">
                {new Date(connector.lastSyncedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
              </span>
              <span className="im-stat-label">Last sync</span>
            </div>
          )}
        </div>
      )}

      {/* Error message */}
      {connector.errorMessage && (
        <div className="im-card-error">{connector.errorMessage}</div>
      )}

      {/* Action button */}
      <div className="im-card-actions">
        {isConnected ? (
          <button className="im-btn im-btn--disconnect" onClick={() => onDisconnect(connector.id)}>
            Disconnect
          </button>
        ) : (
          <button
            className="im-btn im-btn--connect"
            onClick={() => onConnect(connector.id)}
            disabled={isLoading}
          >
            {isLoading ? (
              <>
                <div className="phase-spinner" />
                Connecting...
              </>
            ) : (
              'Connect'
            )}
          </button>
        )}
      </div>
    </div>
  );
}
