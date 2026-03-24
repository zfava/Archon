import type { TaskGraphNode, SimulatedNodeResult } from '../../command/types';

interface Props {
  node: TaskGraphNode;
  simulation: SimulatedNodeResult | undefined;
  onClose: () => void;
}

export function TaskNodeDetail({ node, simulation, onClose }: Props) {
  return (
    <div className="sg-detail" onClick={(e) => e.stopPropagation()}>
      <div className="sg-detail-header">
        <h3 className="sg-detail-title">{node.name}</h3>
        <button className="sg-detail-close" onClick={onClose} aria-label="Close">
          &times;
        </button>
      </div>

      <p className="sg-detail-desc">{node.description}</p>

      {/* Agent Assignment */}
      <div className="sg-detail-section">
        <span className="sg-detail-label">Agent Assignment</span>
        <div className="sg-detail-agent">
          <span className="sg-detail-agent-type">{node.agentType}</span>
          <span className="sg-detail-status" data-status={node.status.toLowerCase()}>
            {node.status}
          </span>
        </div>
      </div>

      {/* Timing */}
      <div className="sg-detail-section">
        <span className="sg-detail-label">Estimated Duration</span>
        <span className="sg-detail-value">{node.estimatedDurationHours}h</span>
      </div>

      <div className="sg-detail-section">
        <span className="sg-detail-label">Priority</span>
        <span className="sg-detail-value">{node.priority}</span>
      </div>

      {/* Required Inputs */}
      {Object.keys(node.requiredInputs).length > 0 && (
        <div className="sg-detail-section">
          <span className="sg-detail-label">Required Inputs</span>
          <div className="sg-detail-inputs">
            {Object.entries(node.requiredInputs).map(([key, value]) => (
              <div key={key} className="sg-detail-input-row">
                <span className="sg-detail-input-key">{key}</span>
                <span className="sg-detail-input-val">{value}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Expected Output */}
      <div className="sg-detail-section">
        <span className="sg-detail-label">Expected Output</span>
        <span className="sg-detail-value">{node.expectedOutput}</span>
      </div>

      {/* Simulation Results */}
      {simulation && (
        <>
          <div className="sg-detail-divider" />
          <div className="sg-detail-section">
            <span className="sg-detail-label">Simulation Results</span>
            <div className="sg-detail-sim-grid">
              <div className="sg-detail-sim-stat">
                <span className="sg-detail-sim-val">
                  {Math.round(simulation.successProbability * 100)}%
                </span>
                <span className="sg-detail-sim-lbl">Success</span>
              </div>
              <div className="sg-detail-sim-stat">
                <span className="sg-detail-sim-val">${simulation.estimatedCost.toFixed(2)}</span>
                <span className="sg-detail-sim-lbl">Cost</span>
              </div>
              <div className="sg-detail-sim-stat">
                <span className="sg-detail-sim-val">
                  {simulation.estimatedDurationHours.toFixed(1)}h
                </span>
                <span className="sg-detail-sim-lbl">Duration</span>
              </div>
            </div>
          </div>

          {simulation.isOnCriticalPath && (
            <div className="sg-detail-section">
              <span className="sg-detail-critical-badge">On Critical Path</span>
            </div>
          )}

          {simulation.nodeRisks.length > 0 && (
            <div className="sg-detail-section">
              <span className="sg-detail-label">Node Risks</span>
              <ul className="sg-detail-risks">
                {simulation.nodeRisks.map((risk, i) => (
                  <li key={i}>{risk}</li>
                ))}
              </ul>
            </div>
          )}
        </>
      )}
    </div>
  );
}
