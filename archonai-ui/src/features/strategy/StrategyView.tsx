import { useState, useMemo } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useStrategy } from './hooks/useStrategy';
import { TaskGraphVisualization } from './components/TaskGraphVisualization';
import { TaskNodeDetail } from './components/TaskNodeDetail';
import { ConfidenceBadge } from '../command/components/ConfidenceBadge';
import './strategy.css';

export function StrategyView() {
  const { goalId } = useParams<{ goalId: string }>();
  const { loading, error, goal, plan, graph } = useStrategy(goalId);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);

  const sim = plan?.selectedSimulation;

  const selectedNode = useMemo(
    () => graph?.nodes.find((n) => n.nodeId === selectedNodeId) ?? null,
    [graph, selectedNodeId],
  );

  const selectedNodeSim = useMemo(
    () => sim?.nodeResults.find((r) => r.nodeId === selectedNodeId),
    [sim, selectedNodeId],
  );

  if (loading) {
    return (
      <div className="sg-container">
        <div className="sg-loading">
          <div className="phase-spinner" />
          <span>Loading strategy...</span>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="sg-container">
        <div className="sg-error">
          <p>{error}</p>
          <Link to="/" className="sg-back-link">Back to Console</Link>
        </div>
      </div>
    );
  }

  if (!goal || !plan || !graph) {
    return (
      <div className="sg-container">
        <div className="sg-error">
          <p>No strategy data available.</p>
          <Link to="/" className="sg-back-link">Back to Console</Link>
        </div>
      </div>
    );
  }

  return (
    <div className="sg-container">
      {/* Header */}
      <header className="sg-header">
        <Link to="/" className="sg-back-link">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M19 12H5M12 19l-7-7 7-7" />
          </svg>
          Console
        </Link>
        <h1 className="sg-page-title">Strategy View</h1>
      </header>

      <div className="sg-content">
        {/* Left: Goal + Strategy summary */}
        <div className="sg-sidebar">
          {/* Goal Section */}
          <section className="sg-card">
            <span className="response-label">Goal</span>
            <h2 className="sg-card-title">{goal.title}</h2>
            <p className="sg-card-desc">{goal.description}</p>
            <div className="response-meta">
              <span className={`priority-tag priority-${goal.priority.toLowerCase()}`}>
                {goal.priority}
              </span>
              <span className="meta-chip">{goal.department}</span>
              <span className="meta-chip">{goal.status}</span>
            </div>
            {goal.deadline && (
              <p className="sg-card-deadline">
                Deadline: {new Date(goal.deadline).toLocaleDateString()}
              </p>
            )}
          </section>

          {/* Strategy Section */}
          {sim && (
            <section className="sg-card">
              <span className="response-label">Strategy</span>
              <div className="strategy-header">
                <span className="strategy-name">{sim.strategy}</span>
                <ConfidenceBadge
                  value={sim.expectedOutcome.overallSuccessProbability}
                  label="Success"
                />
              </div>
              {plan.planDecisionReason && (
                <p className="sg-card-desc">{plan.planDecisionReason}</p>
              )}

              <div className="sg-stats-row">
                <div className="sg-stat">
                  <span className="stat-value">
                    {Math.round(sim.expectedOutcome.overallSuccessProbability * 100)}%
                  </span>
                  <span className="stat-label">Success</span>
                </div>
                <div className="sg-stat">
                  <span className="stat-value">{sim.riskLevel}</span>
                  <span className="stat-label">Risk</span>
                </div>
                <div className="sg-stat">
                  <span className="stat-value">{sim.estimatedTotalDurationHours.toFixed(1)}h</span>
                  <span className="stat-label">Duration</span>
                </div>
                <div className="sg-stat">
                  <span className="stat-value">${sim.estimatedTotalCost.toFixed(2)}</span>
                  <span className="stat-label">Cost</span>
                </div>
              </div>

              {/* Risks */}
              {sim.risks.length > 0 && (
                <div className="sg-risks-section">
                  <span className="response-label">Risks</span>
                  <ul className="risk-list">
                    {sim.risks.map((r, i) => (
                      <li key={i}>{r}</li>
                    ))}
                  </ul>
                </div>
              )}
            </section>
          )}

          {/* Graph stats */}
          <section className="sg-card">
            <span className="response-label">Task Graph</span>
            <div className="sg-stats-row">
              <div className="sg-stat">
                <span className="stat-value">{graph.nodes.length}</span>
                <span className="stat-label">Nodes</span>
              </div>
              <div className="sg-stat">
                <span className="stat-value">{graph.edges.length}</span>
                <span className="stat-label">Dependencies</span>
              </div>
              {sim && (
                <>
                  <div className="sg-stat">
                    <span className="stat-value">{sim.expectedOutcome.criticalPathLength}</span>
                    <span className="stat-label">Critical Path</span>
                  </div>
                  <div className="sg-stat">
                    <span className="stat-value">{sim.expectedOutcome.parallelismDegree}</span>
                    <span className="stat-label">Parallelism</span>
                  </div>
                </>
              )}
            </div>
            <p className="sg-graph-hint">Click a node to inspect its details and agent assignment.</p>
          </section>
        </div>

        {/* Right: Graph visualization + Node detail */}
        <div className="sg-main">
          <TaskGraphVisualization
            graph={graph}
            selectedNodeId={selectedNodeId}
            onSelectNode={setSelectedNodeId}
          />

          {selectedNode && (
            <TaskNodeDetail
              node={selectedNode}
              simulation={selectedNodeSim}
              onClose={() => setSelectedNodeId(null)}
            />
          )}
        </div>
      </div>
    </div>
  );
}
