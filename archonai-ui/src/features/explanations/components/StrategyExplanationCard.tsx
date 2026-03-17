import type { StrategyExplanation } from '../types';

interface Props {
  explanation: StrategyExplanation;
}

function impactClass(impact: string): string {
  switch (impact) {
    case 'High': return 'ex-impact--high';
    case 'Medium': return 'ex-impact--medium';
    default: return 'ex-impact--low';
  }
}

export function StrategyExplanationCard({ explanation }: Props) {
  return (
    <section className="ex-card">
      <div className="ex-card-header">
        <span className="ex-section-label">Why This Strategy?</span>
        <span className="ex-badge ex-badge--strategy">{explanation.chosenStrategy}</span>
      </div>

      <p className="ex-rationale">{explanation.selectionRationale}</p>

      <div className="ex-score-row">
        <span className="ex-score-label">Economic Score</span>
        <div className="ex-score-bar-bg">
          <div
            className="ex-score-bar-fill"
            style={{ width: `${Math.min(explanation.economicScore * 100, 100)}%` }}
          />
        </div>
        <span className="ex-score-value">{(explanation.economicScore * 100).toFixed(0)}</span>
      </div>

      {/* Factors */}
      {explanation.factors.length > 0 && (
        <>
          <span className="ex-sub-label">Decision Factors</span>
          <div className="ex-factors">
            {explanation.factors.map((f) => (
              <div key={f.name} className="ex-factor-row">
                <div className="ex-factor-header">
                  <span className="ex-factor-name">{f.name}</span>
                  <span className={`ex-factor-impact ${impactClass(f.impact)}`}>{f.impact}</span>
                  <span className="ex-factor-score">{(f.score * 100).toFixed(0)}</span>
                </div>
                <p className="ex-factor-desc">{f.description}</p>
                <div className="ex-factor-bar-bg">
                  <div
                    className="ex-factor-bar-fill"
                    style={{ width: `${Math.min(f.score * 100, 100)}%` }}
                  />
                </div>
              </div>
            ))}
          </div>
        </>
      )}

      {/* Alternatives */}
      {explanation.alternatives.length > 0 && (
        <>
          <span className="ex-sub-label">Alternatives Considered</span>
          <div className="ex-alt-list">
            {explanation.alternatives.map((alt) => (
              <div key={alt.strategy} className="ex-alt-row">
                <div className="ex-alt-header">
                  <span className="ex-alt-name">{alt.strategy}</span>
                  <span className="ex-alt-score">{(alt.economicScore * 100).toFixed(0)}</span>
                </div>
                <div className="ex-alt-stats">
                  <span>Success: {(alt.successProbability * 100).toFixed(0)}%</span>
                  <span>Cost: ${alt.estimatedCost.toFixed(2)}</span>
                  <span>Duration: {alt.estimatedDurationHours.toFixed(1)}h</span>
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
