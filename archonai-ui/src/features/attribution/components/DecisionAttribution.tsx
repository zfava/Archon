import type { OutcomeEvaluationResult } from '../types';

interface Props {
  evaluations: OutcomeEvaluationResult[];
}

function assessmentColor(assessment: string): string {
  const lower = assessment.toLowerCase();
  if (lower.includes('success') || lower.includes('excellent')) return '#4ade80';
  if (lower.includes('partial') || lower.includes('mixed') || lower.includes('moderate')) return '#facc15';
  if (lower.includes('fail') || lower.includes('poor')) return '#ef4444';
  return '#a1a1aa';
}

export function DecisionAttribution({ evaluations }: Props) {
  if (evaluations.length === 0) {
    return (
      <div className="at-section">
        <h3 className="at-section-title">Which Decisions Impacted Outcome</h3>
        <p className="at-empty-text">No outcome evaluations available yet. Execute a strategy to see decision impact analysis.</p>
      </div>
    );
  }

  // Use the most recent evaluation
  const latest = evaluations[evaluations.length - 1];
  const cmp = latest.comparison;
  const metrics = latest.successMetrics;

  return (
    <div className="at-section">
      <h3 className="at-section-title">Which Decisions Impacted Outcome</h3>

      {/* Overall assessment */}
      <div className="at-decision-overview">
        <div className="at-decision-assessment">
          <span className="at-detail-label">Overall Assessment</span>
          <span
            className="at-assessment-text"
            style={{ color: assessmentColor(latest.overallAssessment) }}
          >
            {latest.overallAssessment}
          </span>
        </div>

        <div className="at-decision-score-row">
          <div className="at-decision-score">
            <span className="at-score-val">{(metrics.overallScore * 100).toFixed(0)}%</span>
            <span className="at-score-label">Overall Score</span>
          </div>
          <div className="at-decision-score">
            <span className="at-score-val">{metrics.succeededNodes}/{metrics.totalNodes}</span>
            <span className="at-score-label">Nodes Succeeded</span>
          </div>
          <div className="at-decision-score">
            <span className="at-score-val">{(metrics.successRate * 100).toFixed(0)}%</span>
            <span className="at-score-label">Success Rate</span>
          </div>
        </div>
      </div>

      {/* Expected vs Actual */}
      <div className="at-expected-actual">
        <span className="at-detail-label">Expected vs Actual</span>
        <div className="at-cmp-grid">
          <div className="at-cmp-item">
            <span className="at-cmp-metric">Success</span>
            <span className="at-cmp-expected">{(cmp.expectedSuccessProbability * 100).toFixed(0)}% predicted</span>
            <span className={`at-cmp-actual ${cmp.actualSuccess ? 'at-cmp--ok' : 'at-cmp--fail'}`}>
              {cmp.actualSuccess ? 'Succeeded' : 'Failed'}
            </span>
          </div>
          <div className="at-cmp-item">
            <span className="at-cmp-metric">Duration</span>
            <span className="at-cmp-expected">{cmp.expectedDurationHours.toFixed(1)}h expected</span>
            <span className="at-cmp-actual">
              {cmp.actualDurationHours.toFixed(1)}h actual
              <span className={`at-cmp-dev ${cmp.durationDeviationPercent > 0 ? 'at-cmp--warn' : 'at-cmp--ok'}`}>
                {' '}({cmp.durationDeviationPercent > 0 ? '+' : ''}{cmp.durationDeviationPercent.toFixed(0)}%)
              </span>
            </span>
          </div>
          <div className="at-cmp-item">
            <span className="at-cmp-metric">Cost</span>
            <span className="at-cmp-expected">${Number(cmp.expectedCost).toFixed(2)} expected</span>
            <span className="at-cmp-actual">
              ${Number(cmp.actualCost).toFixed(2)} actual
              <span className={`at-cmp-dev ${cmp.costDeviationPercent > 0 ? 'at-cmp--warn' : 'at-cmp--ok'}`}>
                {' '}({cmp.costDeviationPercent > 0 ? '+' : ''}{cmp.costDeviationPercent.toFixed(0)}%)
              </span>
            </span>
          </div>
          <div className="at-cmp-item">
            <span className="at-cmp-metric">Risk Prediction</span>
            <span className="at-cmp-expected">{(cmp.expectedRiskScore * 100).toFixed(0)}% risk score</span>
            <span className={`at-cmp-actual ${cmp.riskPredictionCorrect ? 'at-cmp--ok' : 'at-cmp--fail'}`}>
              {cmp.riskPredictionCorrect ? 'Accurate' : 'Inaccurate'}
            </span>
          </div>
        </div>
      </div>

      {/* Insights */}
      {latest.insights.length > 0 && (
        <div className="at-insights">
          <span className="at-detail-label">Key Insights</span>
          <ul className="at-insight-list">
            {latest.insights.map((insight, i) => <li key={i}>{insight}</li>)}
          </ul>
        </div>
      )}

      {/* Recommendations */}
      {latest.recommendations.length > 0 && (
        <div className="at-recs">
          <span className="at-detail-label">Recommendations</span>
          <ul className="at-rec-list">
            {latest.recommendations.map((rec, i) => <li key={i}>{rec}</li>)}
          </ul>
        </div>
      )}
    </div>
  );
}
