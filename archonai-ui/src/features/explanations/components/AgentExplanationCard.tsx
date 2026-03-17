import type { AgentExplanation } from '../types';

interface Props {
  explanation: AgentExplanation;
}

function impactClass(impact: string): string {
  switch (impact) {
    case 'High': return 'ex-impact--high';
    case 'Medium': return 'ex-impact--medium';
    default: return 'ex-impact--low';
  }
}

export function AgentExplanationCard({ explanation }: Props) {
  return (
    <section className="ex-card">
      <div className="ex-card-header">
        <span className="ex-section-label">Why This Agent?</span>
        <span className="ex-badge ex-badge--agent">{explanation.selectedAgentName}</span>
      </div>

      <p className="ex-rationale">{explanation.selectionReason}</p>

      <div className="ex-meta-row">
        <span className="ex-meta-item">
          Capability: <strong>{explanation.requiredCapability}</strong>
        </span>
        {explanation.taskType && (
          <span className="ex-meta-item">
            Task Type: <strong>{explanation.taskType}</strong>
          </span>
        )}
      </div>

      <div className="ex-score-row">
        <span className="ex-score-label">Selection Score</span>
        <div className="ex-score-bar-bg">
          <div
            className="ex-score-bar-fill ex-score-bar-fill--agent"
            style={{ width: `${Math.min(explanation.selectionScore * 100, 100)}%` }}
          />
        </div>
        <span className="ex-score-value">{(explanation.selectionScore * 100).toFixed(0)}</span>
      </div>

      {/* Factors */}
      {explanation.factors.length > 0 && (
        <>
          <span className="ex-sub-label">Selection Factors</span>
          <div className="ex-agent-factors">
            {explanation.factors.map((f) => (
              <div key={f.name} className="ex-agent-factor">
                <div className="ex-agent-factor-header">
                  <span className="ex-factor-name">{f.name}</span>
                  <span className={`ex-factor-impact ${impactClass(f.impact)}`}>{f.impact}</span>
                </div>
                <span className="ex-agent-factor-value">{f.value}</span>
                <p className="ex-factor-desc">{f.description}</p>
              </div>
            ))}
          </div>
        </>
      )}

      {/* Alternatives */}
      {explanation.alternatives.length > 0 && (
        <>
          <span className="ex-sub-label">Other Candidates</span>
          <div className="ex-alt-list">
            {explanation.alternatives.map((alt) => (
              <div key={alt.agentId} className="ex-alt-row">
                <div className="ex-alt-header">
                  <span className="ex-alt-name">{alt.agentName}</span>
                  <span className="ex-alt-score">{(alt.score * 100).toFixed(0)}</span>
                </div>
                <div className="ex-alt-stats">
                  <span>Success: {(alt.successRate * 100).toFixed(0)}%</span>
                  <span>Latency: {alt.averageLatencyMs.toFixed(0)}ms</span>
                  <span>Cost: ${alt.averageCost.toFixed(2)}</span>
                </div>
                <p className="ex-alt-reason">{alt.whyNotChosen}</p>
              </div>
            ))}
          </div>
        </>
      )}
    </section>
  );
}
