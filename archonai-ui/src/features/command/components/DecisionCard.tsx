import { useState } from 'react';
import { ConfidenceBadge } from './ConfidenceBadge';
import { RiskIndicator } from './RiskIndicator';
import type { DecisionSummary } from '../types';

interface Props {
  decision: DecisionSummary;
  title?: string;
}

export function DecisionCard({ decision, title }: Props) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="decision-card">
      {/* Header with confidence + risk side by side */}
      <div className="decision-header">
        <div className="decision-title-row">
          {title && <span className="response-label">{title}</span>}
        </div>
        <div className="decision-scores">
          {/* Confidence */}
          <div className="decision-score-block">
            <span className="decision-score-label">Confidence</span>
            <div className="decision-confidence-row">
              <div className="decision-conf-bar-wrap">
                <div
                  className="decision-conf-bar"
                  data-level={decision.confidenceScore >= 0.8 ? 'high' : decision.confidenceScore >= 0.5 ? 'medium' : 'low'}
                  style={{ width: `${Math.round(decision.confidenceScore * 100)}%` }}
                />
              </div>
              <ConfidenceBadge value={decision.confidenceScore} />
            </div>
          </div>

          {/* Risk */}
          <div className="decision-score-block">
            <span className="decision-score-label">Risk</span>
            <RiskIndicator score={decision.riskScore} level={decision.riskLevel} size="sm" />
          </div>
        </div>
      </div>

      {/* "Why this decision?" — always visible */}
      <div className="decision-reasoning">
        <button
          className="decision-why-btn"
          onClick={() => setExpanded(!expanded)}
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            className={expanded ? 'decision-chevron--open' : ''}
          >
            <path d="M9 18l6-6-6-6" />
          </svg>
          Why this decision?
        </button>
        <p className="decision-reason-text">{decision.reasoning}</p>
      </div>

      {/* Expanded detail */}
      {expanded && (
        <div className="decision-detail">
          {/* Assessment */}
          {decision.assessment && (
            <div className="decision-detail-section">
              <span className="decision-detail-label">Assessment</span>
              <span
                className="decision-assessment-badge"
                data-assessment={decision.assessment.toLowerCase()}
              >
                {decision.assessment}
              </span>
            </div>
          )}

          {/* Insights */}
          {decision.insights.length > 0 && (
            <div className="decision-detail-section">
              <span className="decision-detail-label">Insights</span>
              <ul className="decision-insight-list">
                {decision.insights.map((insight, i) => (
                  <li key={i}>{insight}</li>
                ))}
              </ul>
            </div>
          )}

          {/* Recommendations */}
          {decision.recommendations.length > 0 && (
            <div className="decision-detail-section">
              <span className="decision-detail-label">Recommendations</span>
              <ul className="decision-rec-list">
                {decision.recommendations.map((rec, i) => (
                  <li key={i}>{rec}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
