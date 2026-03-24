import type { SimulationGuidedPlan } from '../types';

interface Props {
  plan: SimulationGuidedPlan;
}

export function StrategyAttribution({ plan }: Props) {
  const sim = plan.selectedSimulation;
  const comparison = plan.comparisonResult;
  const alternativeCount = comparison.simulations.length - 1;

  return (
    <div className="at-section">
      <h3 className="at-section-title">Which Strategy Caused This Result</h3>

      <div className="at-strategy-card">
        <div className="at-strategy-header">
          <span className="at-strategy-name">{plan.selectedStrategy}</span>
          <span className="at-strategy-badge at-badge--selected">Selected</span>
        </div>

        <p className="at-strategy-reason">{plan.planDecisionReason}</p>

        <div className="at-strategy-stats">
          <div className="at-stat">
            <span className="at-stat-val">{Math.round(sim.expectedOutcome.overallSuccessProbability * 100)}%</span>
            <span className="at-stat-label">Success Probability</span>
          </div>
          <div className="at-stat">
            <span className="at-stat-val">{(sim.riskScore * 100).toFixed(0)}%</span>
            <span className="at-stat-label">Risk Score</span>
          </div>
          <div className="at-stat">
            <span className="at-stat-val">{sim.estimatedTotalDurationHours.toFixed(1)}h</span>
            <span className="at-stat-label">Duration</span>
          </div>
          <div className="at-stat">
            <span className="at-stat-val">${sim.estimatedTotalCost.toFixed(2)}</span>
            <span className="at-stat-label">Cost</span>
          </div>
        </div>

        {sim.risks.length > 0 && (
          <div className="at-strategy-risks">
            <span className="at-detail-label">Identified Risks</span>
            <ul className="at-risk-list">
              {sim.risks.map((r, i) => <li key={i}>{r}</li>)}
            </ul>
          </div>
        )}
      </div>

      {/* Alternative strategies considered */}
      {alternativeCount > 0 && (
        <div className="at-alternatives">
          <span className="at-detail-label">{alternativeCount} Alternative{alternativeCount > 1 ? 's' : ''} Considered</span>
          <p className="at-alt-reason">{comparison.recommendationReason}</p>
          <div className="at-alt-list">
            {comparison.simulations
              .filter((s) => s.simulationId !== sim.simulationId)
              .map((s) => (
                <div key={s.simulationId} className="at-alt-card">
                  <span className="at-alt-name">{s.strategy}</span>
                  <span className="at-alt-stat">{Math.round(s.expectedOutcome.overallSuccessProbability * 100)}% success</span>
                  <span className="at-alt-stat">{s.riskLevel} risk</span>
                </div>
              ))}
          </div>
        </div>
      )}
    </div>
  );
}
