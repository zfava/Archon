import { Link } from 'react-router-dom';
import { useDashboardHub } from './hooks/useDashboardHub';
import { ConnectionBadge } from './components/ConnectionBadge';
import { AgentPanel } from './components/AgentPanel';
import { TaskPanel } from './components/TaskPanel';
import { ActivityFeed } from './components/ActivityFeed';
import './activity.css';

export function SystemActivityView() {
  const {
    connectionStatus,
    agentActivity,
    taskPerformance,
    systemHealth,
    alerts,
    lastUpdated,
    refresh,
  } = useDashboardHub();

  const recentEvents = agentActivity?.recentEvents ?? [];
  const allAlerts = [
    ...alerts,
    ...(systemHealth?.activeAlerts ?? []),
  ].filter(
    (a, i, arr) => arr.findIndex((b) => b.alertId === a.alertId) === i,
  );

  return (
    <div className="sa-container">
      {/* Header */}
      <header className="sa-header">
        <div className="sa-header-left">
          <Link to="/" className="sg-back-link">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
            Console
          </Link>
          <h1 className="sa-page-title">System Activity</h1>
        </div>
        <div className="sa-header-right">
          <button
            className="sa-refresh-btn"
            onClick={refresh}
            disabled={connectionStatus !== 'connected'}
            title="Refresh dashboard"
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M23 4v6h-6M1 20v-6h6" />
              <path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15" />
            </svg>
          </button>
          <ConnectionBadge status={connectionStatus} lastUpdated={lastUpdated} />
        </div>
      </header>

      {/* Health bar */}
      {systemHealth && (
        <div className="sa-health-bar" data-status={systemHealth.overallStatus.toLowerCase()}>
          <span className="sa-health-label">System</span>
          <span className="sa-health-status">{systemHealth.overallStatus}</span>
          <span className="sa-health-sep" />
          <span className="sa-health-metric">
            CPU {Math.round(systemHealth.resources.cpuPercent)}%
          </span>
          <span className="sa-health-metric">
            Mem {Math.round(systemHealth.resources.memoryPercent)}%
          </span>
          <span className="sa-health-metric">
            Threads {systemHealth.resources.activeThreads}
          </span>
          <span className="sa-health-metric">
            Pending {systemHealth.resources.pendingWorkItems}
          </span>
          {systemHealth.cluster && (
            <>
              <span className="sa-health-sep" />
              <span className="sa-health-metric">
                {systemHealth.cluster.activeNodes}/{systemHealth.cluster.totalNodes} nodes
              </span>
            </>
          )}
        </div>
      )}

      {/* Main grid */}
      <div className="sa-grid">
        <AgentPanel data={agentActivity} />
        <TaskPanel data={taskPerformance} />
        <ActivityFeed events={recentEvents} alerts={allAlerts} />
      </div>
    </div>
  );
}
