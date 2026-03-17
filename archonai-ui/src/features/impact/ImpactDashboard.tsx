import { Link } from 'react-router-dom';
import { useImpactDashboard } from './hooks/useImpactDashboard';
import { ImpactMetricCard } from './components/ImpactMetricCard';
import { BeforeAfterChart } from './components/BeforeAfterChart';
import { TrendSpark } from './components/TrendSpark';
import { ResourceGauge } from './components/ResourceGauge';
import './impact.css';

export function ImpactDashboard() {
  const {
    loading,
    error,
    connectionStatus,
    taskPerformance,
    agentActivity,
    modelUsage,
    systemHealth,
    impactMetrics,
    beforeAfter,
    lastUpdated,
    refresh,
  } = useImpactDashboard();

  const revenueMetrics = impactMetrics.filter((m) => m.category === 'revenue');
  const costMetrics = impactMetrics.filter((m) => m.category === 'cost');
  const efficiencyMetrics = impactMetrics.filter((m) => m.category === 'efficiency');

  return (
    <div className="im-view">
      {/* Header */}
      <header className="im-header">
        <div className="im-header-left">
          <Link to="/" className="im-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M19 12H5M12 19l-7-7 7-7" />
            </svg>
          </Link>
          <div>
            <h1 className="im-title">Business Impact</h1>
            <p className="im-subtitle">Revenue, cost savings, and efficiency gains from ArchonAI</p>
          </div>
        </div>
        <div className="im-header-right">
          <span className={`im-live-dot im-live-dot--${connectionStatus}`} />
          <span className="im-live-label">
            {connectionStatus === 'connected' ? 'Live' : connectionStatus}
          </span>
          {lastUpdated && (
            <span className="im-last-update">
              {new Date(lastUpdated).toLocaleTimeString()}
            </span>
          )}
          <button className="im-refresh-btn" onClick={refresh} disabled={loading}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M23 4v6h-6M1 20v-6h6" />
              <path d="M3.51 9a9 9 0 0114.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0020.49 15" />
            </svg>
          </button>
        </div>
      </header>

      {/* Error */}
      {error && (
        <div className="im-error">{error}</div>
      )}

      {/* Loading */}
      {loading && !taskPerformance ? (
        <div className="im-loading">
          <div className="im-loading-bar" />
          <div className="im-loading-bar" />
          <div className="im-loading-bar" />
        </div>
      ) : (
        <div className="im-scroll">
          {/* Summary strip */}
          {taskPerformance && (
            <div className="im-summary-strip">
              <div className="im-summary-item">
                <span className="im-summary-val">{taskPerformance.completedTasks.toLocaleString()}</span>
                <span className="im-summary-label">Tasks Completed</span>
              </div>
              <div className="im-summary-item">
                <span className="im-summary-val">{(taskPerformance.overallCompletionRate * 100).toFixed(1)}%</span>
                <span className="im-summary-label">Completion Rate</span>
              </div>
              {agentActivity && (
                <div className="im-summary-item">
                  <span className="im-summary-val">{agentActivity.activeAgents}</span>
                  <span className="im-summary-label">Active Agents</span>
                </div>
              )}
              {modelUsage && (
                <div className="im-summary-item">
                  <span className="im-summary-val">${modelUsage.totalCost.toFixed(2)}</span>
                  <span className="im-summary-label">Total AI Cost</span>
                </div>
              )}
              {systemHealth && (
                <div className="im-summary-item">
                  <span className="im-summary-val">{(systemHealth.healthScore * 100).toFixed(0)}%</span>
                  <span className="im-summary-label">Health Score</span>
                </div>
              )}
            </div>
          )}

          {/* Revenue Impact */}
          {revenueMetrics.length > 0 && (
            <section className="im-section">
              <h2 className="im-section-title">Revenue Impact</h2>
              <div className="im-metrics-grid">
                {revenueMetrics.map((m) => (
                  <ImpactMetricCard key={m.label} metric={m} />
                ))}
              </div>
            </section>
          )}

          {/* Cost Savings */}
          {costMetrics.length > 0 && (
            <section className="im-section">
              <h2 className="im-section-title">Cost Savings</h2>
              <div className="im-metrics-grid">
                {costMetrics.map((m) => (
                  <ImpactMetricCard key={m.label} metric={m} />
                ))}
              </div>
            </section>
          )}

          {/* Efficiency Gains */}
          {efficiencyMetrics.length > 0 && (
            <section className="im-section">
              <h2 className="im-section-title">Efficiency Gains</h2>
              <div className="im-metrics-grid">
                {efficiencyMetrics.map((m) => (
                  <ImpactMetricCard key={m.label} metric={m} />
                ))}
              </div>
            </section>
          )}

          {/* Before vs After */}
          <section className="im-section">
            <h2 className="im-section-title">Before vs After ArchonAI</h2>
            <BeforeAfterChart metrics={beforeAfter} />
          </section>

          {/* Bottom row: Trends + Resources */}
          <div className="im-bottom-row">
            {taskPerformance && taskPerformance.trends.length > 0 && (
              <TrendSpark trends={taskPerformance.trends} title="Task Performance Trends" />
            )}
            {modelUsage && modelUsage.trends.length > 0 && (
              <TrendSpark trends={modelUsage.trends} title="Model Usage Trends" />
            )}
            {systemHealth && (
              <ResourceGauge health={systemHealth} />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
