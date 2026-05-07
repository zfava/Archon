import type { AgentActivityDashboard } from '../types';

interface Props {
  data: AgentActivityDashboard | null;
}

function statusIndicator(status: string): string {
  switch (status.toLowerCase()) {
    case 'active': return 'sa-dot--active';
    case 'idle': return 'sa-dot--idle';
    case 'draining': return 'sa-dot--draining';
    default: return 'sa-dot--offline';
  }
}

export function AgentPanel({ data }: Props) {
  if (!data) {
    return (
      <section className="sa-panel">
        <h2 className="sa-panel-title">Active Agents</h2>
        <div className="sa-panel-empty">Waiting for data...</div>
      </section>
    );
  }

  return (
    <section className="sa-panel">
      <div className="sa-panel-header">
        <h2 className="sa-panel-title">Active Agents</h2>
        <div className="sa-panel-counts">
          <span className="sa-count sa-count--active">{data.activeAgents} active</span>
          <span className="sa-count sa-count--idle">{data.idleAgents} idle</span>
          {data.drainingAgents > 0 && (
            <span className="sa-count sa-count--drain">{data.drainingAgents} draining</span>
          )}
        </div>
      </div>

      {/* Summary bar */}
      <div className="sa-agent-summary">
        <div className="sa-summary-stat">
          <span className="sa-summary-val">{data.totalRegistered}</span>
          <span className="sa-summary-lbl">Registered</span>
        </div>
        <div className="sa-summary-stat">
          <span className="sa-summary-val">{data.totalExecutions.toLocaleString()}</span>
          <span className="sa-summary-lbl">Executions</span>
        </div>
        <div className="sa-summary-stat">
          <span className="sa-summary-val">{Math.round(data.overallSuccessRate * 100)}%</span>
          <span className="sa-summary-lbl">Success Rate</span>
        </div>
      </div>

      {/* Agent list */}
      <div className="sa-agent-list">
        {data.agents.map((agent) => (
          <div key={agent.agentId} className="sa-agent-row">
            <div className="sa-agent-main">
              <span className={`sa-dot ${statusIndicator(agent.status)}`} />
              <div className="sa-agent-info">
                <span className="sa-agent-name">{agent.name}</span>
                <span className="sa-agent-caps">
                  {agent.capabilities.slice(0, 3).join(', ')}
                  {agent.capabilities.length > 3 && ` +${agent.capabilities.length - 3}`}
                </span>
              </div>
            </div>
            <div className="sa-agent-metrics">
              <span className="sa-agent-tasks" title="Active tasks">
                {agent.activeTasks} tasks
              </span>
              <span className="sa-agent-latency" title="Avg latency">
                {Math.round(agent.averageLatencyMs)}ms
              </span>
              <span className="sa-agent-rate" title="Success rate">
                {Math.round(agent.successRate * 100)}%
              </span>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
