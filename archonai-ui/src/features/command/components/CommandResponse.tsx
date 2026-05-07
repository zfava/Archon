import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../../api/client';
import type { CommandResult, DecisionSummary, OutcomeEvaluationResult } from '../types';
import { ConfidenceBadge } from './ConfidenceBadge';
import { DecisionCard } from './DecisionCard';

interface Props {
  result: CommandResult;
}

/** Build a DecisionSummary from simulation + optional outcome evaluation. */
function buildDecision(
  result: CommandResult,
  outcomeEval: OutcomeEvaluationResult | null,
): DecisionSummary | null {
  const sim = result.plan?.selectedSimulation;
  if (!sim) return null;

  // If we have a Reasoner outcome evaluation, use it for richer data
  if (outcomeEval) {
    return {
      confidenceScore: outcomeEval.successMetrics.overallScore,
      riskScore: outcomeEval.comparison.expectedRiskScore,
      riskLevel: sim.riskLevel,
      reasoning: result.plan?.planDecisionReason ?? outcomeEval.overallAssessment,
      insights: outcomeEval.insights,
      recommendations: outcomeEval.recommendations,
      assessment: outcomeEval.overallAssessment,
    };
  }

  // Fall back to simulation data
  return {
    confidenceScore: sim.expectedOutcome.confidence,
    riskScore: sim.riskScore,
    riskLevel: sim.riskLevel,
    reasoning:
      result.plan?.planDecisionReason ??
      result.comparison?.recommendationReason ??
      `Strategy "${sim.strategy}" selected with ${Math.round(sim.expectedOutcome.overallSuccessProbability * 100)}% predicted success.`,
    insights: sim.warnings,
    recommendations: [],
    assessment: sim.expectedOutcome.predictedOutcomeLabel,
  };
}

export function CommandResponse({ result }: Props) {
  const { goal, plan, comparison, phase, error } = result;
  const [outcomeEval, setOutcomeEval] = useState<OutcomeEvaluationResult | null>(null);

  // Fetch Reasoner outcome evaluation when plan is available
  useEffect(() => {
    if (!goal || !plan) return;

    let cancelled = false;
    (async () => {
      try {
        const evals = (await api.getOutcomeEvaluationsForGoal(
          goal.goalId,
        )) as OutcomeEvaluationResult[];
        if (!cancelled && evals.length > 0) {
          // Use the most recent evaluation
          setOutcomeEval(evals[evals.length - 1]);
        }
      } catch {
        // Outcome evaluations are optional — fail silently
      }
    })();

    return () => { cancelled = true; };
  }, [goal, plan]);

  if (phase === 'parsing' || phase === 'planning') {
    return (
      <div className="cmd-response cmd-response--loading">
        <div className="response-skeleton" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="cmd-response cmd-response--error">
        <div className="response-section">
          <span className="response-label">Error</span>
          <p className="response-error-text">{error}</p>
        </div>
      </div>
    );
  }

  if (!goal) return null;

  const sim = plan?.selectedSimulation;
  const decision = buildDecision(result, outcomeEval);

  return (
    <div className="cmd-response">
      {/* Interpreted Goal */}
      <div className="response-section">
        <span className="response-label">Interpreted Goal</span>
        <h3 className="response-title">{goal.title}</h3>
        <p className="response-desc">{goal.description}</p>
        <div className="response-meta">
          <span className={`priority-tag priority-${goal.priority.toLowerCase()}`}>
            {goal.priority}
          </span>
          <span className="meta-chip">{goal.department}</span>
          <span className="meta-chip">{goal.source}</span>
        </div>
      </div>

      {/* Proposed Strategy */}
      {sim && (
        <div className="response-section">
          <span className="response-label">Proposed Strategy</span>
          <div className="strategy-card">
            <div className="strategy-header">
              <span className="strategy-name">{sim.strategy}</span>
              <ConfidenceBadge
                value={sim.expectedOutcome.overallSuccessProbability}
                label="Success probability"
              />
            </div>
            {comparison && (
              <p className="strategy-reason">{comparison.recommendationReason}</p>
            )}
          </div>
        </div>
      )}

      {/* Decision Card — unified confidence + risk + reasoning */}
      {decision && (
        <div className="response-section">
          <DecisionCard decision={decision} title="Decision Analysis" />
        </div>
      )}

      {/* Expected Outcome */}
      {sim && (
        <div className="response-section">
          <span className="response-label">Expected Outcome</span>
          <div className="outcome-grid">
            <div className="outcome-stat">
              <span className="stat-value">
                {Math.round(sim.expectedOutcome.overallSuccessProbability * 100)}%
              </span>
              <span className="stat-label">Success</span>
            </div>
            <div className="outcome-stat">
              <span className="stat-value">{sim.estimatedTotalDurationHours.toFixed(1)}h</span>
              <span className="stat-label">Duration</span>
            </div>
            <div className="outcome-stat">
              <span className="stat-value">${sim.estimatedTotalCost.toFixed(2)}</span>
              <span className="stat-label">Cost</span>
            </div>
            <div className="outcome-stat">
              <span className="stat-value">{sim.expectedOutcome.totalNodes}</span>
              <span className="stat-label">Tasks</span>
            </div>
            <div className="outcome-stat">
              <span className="stat-value">{sim.expectedOutcome.criticalPathLength}</span>
              <span className="stat-label">Critical Path</span>
            </div>
            <div className="outcome-stat">
              <span className="stat-value">{sim.expectedOutcome.parallelismDegree}</span>
              <span className="stat-label">Parallelism</span>
            </div>
          </div>
        </div>
      )}

      {/* Strategy Comparison (if multiple) */}
      {comparison && comparison.simulations.length > 1 && (
        <div className="response-section">
          <span className="response-label">Strategy Comparison</span>
          <div className="comparison-table-wrap">
            <table className="comparison-table">
              <thead>
                <tr>
                  <th>Strategy</th>
                  <th>Confidence</th>
                  <th>Risk</th>
                  <th>Success</th>
                  <th>Duration</th>
                  <th>Cost</th>
                </tr>
              </thead>
              <tbody>
                {comparison.simulations.map((s) => (
                  <tr
                    key={s.simulationId}
                    className={s.simulationId === sim?.simulationId ? 'row-selected' : ''}
                  >
                    <td>{s.strategy}</td>
                    <td>
                      <ConfidenceBadge value={s.expectedOutcome.confidence} label="Confidence" />
                    </td>
                    <td>
                      <span
                        className={`risk-badge risk-badge--${s.riskLevel.toLowerCase()}`}
                      >
                        {s.riskLevel}
                      </span>
                    </td>
                    <td>
                      <ConfidenceBadge value={s.expectedOutcome.overallSuccessProbability} />
                    </td>
                    <td>{s.estimatedTotalDurationHours.toFixed(1)}h</td>
                    <td>${s.estimatedTotalCost.toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Risks */}
      {sim && sim.risks.length > 0 && (
        <div className="response-section">
          <span className="response-label">Risks</span>
          <ul className="risk-list">
            {sim.risks.map((r, i) => (
              <li key={i}>{r}</li>
            ))}
          </ul>
        </div>
      )}

      {/* Dispatch result */}
      {result.dispatch && (
        <div className="response-section">
          <span className="response-label">Execution Status</span>
          <div className="dispatch-summary">
            <span className="dispatch-ok">Dispatched</span>
            <span className="meta-chip">{result.dispatch.totalTasks} tasks</span>
            <span className="meta-chip">{result.dispatch.executionLayers} layers</span>
          </div>
        </div>
      )}

      {/* Strategy visualization + attribution links */}
      {plan && (
        <div className="response-section response-links">
          <Link
            to={`/strategy/${goal.goalId}`}
            className="sg-view-link"
          >
            View Strategy Graph
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M5 12h14M12 5l7 7-7 7" />
            </svg>
          </Link>
          <Link
            to={`/attribution/${goal.goalId}`}
            className="sg-view-link"
          >
            Explain Outcome
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M5 12h14M12 5l7 7-7 7" />
            </svg>
          </Link>
        </div>
      )}
    </div>
  );
}
