import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useProofDashboard, useProofTimeline } from './hooks/useProofAnalytics';
import type { PredictedVsActualEntry, TrustByActionType, ProofEvent } from './types';
import './proof-analytics.css';

function fmtCurrency(n: number): string {
  const abs = Math.abs(n);
  const formatted = abs >= 1_000_000
    ? `$${(abs / 1_000_000).toFixed(1)}M`
    : abs >= 1_000
      ? `$${(abs / 1_000).toFixed(0)}K`
      : `$${abs.toLocaleString()}`;
  return n < 0 ? `-${formatted}` : formatted;
}

function pct(n: number): string {
  return `${Math.round(n * 100)}%`;
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function fmtEventType(type: string): string {
  return type.replace(/([A-Z])/g, ' $1').trim();
}

function eventCssClass(type: string): string {
  const map: Record<string, string> = {
    DecisionCreated: 'decision-created',
    ApprovalGranted: 'approval-granted',
    ApprovalDenied: 'approval-denied',
    ActionExecuted: 'action-executed',
    ActualOutcomeRecorded: 'actual-outcome-recorded',
    OverrideApplied: 'override-applied',
    ReversalApplied: 'reversal-applied',
  };
  return map[type] ?? '';
}

function TimelineDetail({ decisionId, onBack }: { decisionId: string; onBack: () => void }) {
  const { timeline, loading, error } = useProofTimeline(decisionId);

  if (loading) return <div className="proof-loading">Loading timeline...</div>;
  if (error) return <div className="proof-error">{error}</div>;
  if (!timeline) return <div className="proof-empty">No timeline data found</div>;

  const s = timeline.summary;

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 20 }}>
        <button className="proof-refresh" onClick={onBack}>Back</button>
        <div>
          <h2 style={{ margin: 0, fontSize: '1.1rem', color: 'var(--text-1)' }}>{timeline.decisionTitle}</h2>
          <span style={{ fontSize: '0.78rem', color: 'var(--text-3)' }}>{timeline.domain}</span>
        </div>
      </div>

      <div className="proof-kpi-row">
        <div className="proof-kpi">
          <div className="proof-kpi-label">Events</div>
          <div className="proof-kpi-value">{s.totalEvents}</div>
        </div>
        <div className="proof-kpi">
          <div className="proof-kpi-label">Predicted</div>
          <div className="proof-kpi-value">{s.predictedValue != null ? fmtCurrency(s.predictedValue) : '--'}</div>
        </div>
        <div className="proof-kpi">
          <div className="proof-kpi-label">Actual</div>
          <div className="proof-kpi-value">{s.actualValue != null ? fmtCurrency(s.actualValue) : '--'}</div>
        </div>
        <div className="proof-kpi">
          <div className="proof-kpi-label">Variance</div>
          <div className={`proof-kpi-value ${s.variance != null && s.variance >= 0 ? 'positive' : s.variance != null ? 'negative' : 'neutral'}`}>
            {s.variance != null ? fmtCurrency(s.variance) : '--'}
            {s.variancePercent != null && <span className="proof-kpi-sub"> ({s.variancePercent > 0 ? '+' : ''}{s.variancePercent.toFixed(1)}%)</span>}
          </div>
        </div>
        {s.finalAssessment && (
          <div className="proof-kpi">
            <div className="proof-kpi-label">Assessment</div>
            <div className="proof-kpi-value" style={{ fontSize: '1rem' }}>{s.finalAssessment.replace(/([A-Z])/g, ' $1').trim()}</div>
          </div>
        )}
      </div>

      <div className="proof-section">
        <div className="proof-section-title">Decision-to-Outcome Lineage</div>
        <div className="proof-timeline">
          {timeline.events.map((evt: ProofEvent) => (
            <div key={evt.id} className={`proof-timeline-event ${eventCssClass(evt.eventType)}`}>
              <div className="proof-timeline-time">{fmtDate(evt.occurredAtUtc)}</div>
              <div className="proof-timeline-type">{fmtEventType(evt.eventType)}</div>
              {evt.detail && <div className="proof-timeline-detail">{evt.detail}</div>}
              <div className="proof-timeline-values">
                {evt.expectedValue != null && <span>Expected: {fmtCurrency(evt.expectedValue)}</span>}
                {evt.actualValue != null && <span>Actual: {fmtCurrency(evt.actualValue)}</span>}
                {evt.economicImpact != null && <span>Impact: {fmtCurrency(evt.economicImpact)}</span>}
                {evt.isSuccess != null && <span>{evt.isSuccess ? 'Success' : 'Failed'}</span>}
                {evt.overrideReason && <span>Override: {evt.overrideReason}</span>}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Cross-system links */}
      <div className="proof-cross-links">
        <Link to="/inspection" className="proof-cross-link">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
          Inspect Decision
        </Link>
        <Link to="/action-safety" className="proof-cross-link">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9.5 9l5 5m0-5l-5 5"/></svg>
          View Safety
        </Link>
        <Link to="/simulation" className="proof-cross-link">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
          Simulate Similar
        </Link>
      </div>
    </div>
  );
}

export function ProofAnalyticsView() {
  const { dashboard, loading, error, refresh } = useProofDashboard();
  const [selectedDecisionId, setSelectedDecisionId] = useState<string | null>(null);

  if (selectedDecisionId) {
    return (
      <div className="proof-view">
        <TimelineDetail decisionId={selectedDecisionId} onBack={() => setSelectedDecisionId(null)} />
      </div>
    );
  }

  return (
    <div className="proof-view">
      <header className="proof-header">
        <div className="proof-header-left">
          <div>
            <h1 className="proof-title">Proof Analytics</h1>
            <p className="proof-subtitle">Immutable evidence chain: what was predicted, what happened, and why the variance</p>
          </div>
        </div>
        <button className="proof-refresh" onClick={refresh}>Refresh</button>
      </header>

      {loading ? (
        <div className="proof-loading">Loading proof analytics...</div>
      ) : error ? (
        <div className="proof-error">{error}</div>
      ) : !dashboard ? (
        <div className="proof-empty">No proof data available</div>
      ) : (
        <>
          {/* KPI Summary Row */}
          <div className="proof-kpi-row">
            <div className="proof-kpi">
              <div className="proof-kpi-label">Total Decisions</div>
              <div className="proof-kpi-value">{dashboard.predictedVsActual.totalDecisions}</div>
            </div>
            <div className="proof-kpi">
              <div className="proof-kpi-label">With Outcomes</div>
              <div className="proof-kpi-value">{dashboard.predictedVsActual.withOutcomes}</div>
            </div>
            <div className="proof-kpi">
              <div className="proof-kpi-label">Accuracy Rate</div>
              <div className={`proof-kpi-value ${dashboard.predictedVsActual.accuracyRate >= 0.7 ? 'positive' : dashboard.predictedVsActual.accuracyRate >= 0.4 ? 'neutral' : 'negative'}`}>
                {pct(dashboard.predictedVsActual.accuracyRate)}
              </div>
            </div>
            <div className="proof-kpi">
              <div className="proof-kpi-label">Approval Rate</div>
              <div className="proof-kpi-value">{pct(dashboard.approvalConversion.approvalRate)}</div>
            </div>
            <div className="proof-kpi">
              <div className="proof-kpi-label">Execution Success</div>
              <div className={`proof-kpi-value ${dashboard.executionTrends.successRate >= 0.8 ? 'positive' : 'negative'}`}>
                {pct(dashboard.executionTrends.successRate)}
              </div>
            </div>
            <div className="proof-kpi">
              <div className="proof-kpi-label">Override Rate</div>
              <div className={`proof-kpi-value ${dashboard.overrideRates.overrideRate <= 0.1 ? 'positive' : 'negative'}`}>
                {pct(dashboard.overrideRates.overrideRate)}
              </div>
            </div>
          </div>

          {/* Predicted vs Actual */}
          <div className="proof-section">
            <div className="proof-section-title">Predicted vs Actual</div>
            <div className="proof-kpi-row" style={{ marginBottom: 14 }}>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Total Predicted</div>
                <div className="proof-kpi-value">{fmtCurrency(dashboard.predictedVsActual.totalPredictedValue)}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Total Actual</div>
                <div className="proof-kpi-value">{fmtCurrency(dashboard.predictedVsActual.totalActualValue)}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Net Variance</div>
                <div className={`proof-kpi-value ${dashboard.predictedVsActual.totalVariance >= 0 ? 'positive' : 'negative'}`}>
                  {fmtCurrency(dashboard.predictedVsActual.totalVariance)}
                </div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Mean Variance %</div>
                <div className="proof-kpi-value neutral">
                  {dashboard.predictedVsActual.meanVariancePercent.toFixed(1)}%
                </div>
              </div>
            </div>

            {dashboard.predictedVsActual.entries.length > 0 ? (
              <table className="proof-pva-table">
                <thead>
                  <tr>
                    <th>Decision</th>
                    <th>Domain</th>
                    <th>Predicted</th>
                    <th>Actual</th>
                    <th>Variance</th>
                    <th>Direction</th>
                    <th>Date</th>
                  </tr>
                </thead>
                <tbody>
                  {dashboard.predictedVsActual.entries.map((entry: PredictedVsActualEntry) => (
                    <tr key={entry.decisionId} onClick={() => setSelectedDecisionId(entry.decisionId)} style={{ cursor: 'pointer' }}>
                      <td>{entry.title}</td>
                      <td>{entry.domain}</td>
                      <td>{entry.predictedValue != null ? fmtCurrency(entry.predictedValue) : '--'}</td>
                      <td>{entry.actualValue != null ? fmtCurrency(entry.actualValue) : '--'}</td>
                      <td>
                        {entry.variance != null ? fmtCurrency(entry.variance) : '--'}
                        {entry.variancePercent != null && ` (${entry.variancePercent > 0 ? '+' : ''}${entry.variancePercent.toFixed(1)}%)`}
                      </td>
                      <td><span className={`proof-badge ${entry.direction.toLowerCase()}`}>{entry.direction}</span></td>
                      <td>{fmtDate(entry.decisionCreatedAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <div className="proof-empty">No decision entries yet</div>
            )}
          </div>

          {/* Approval to Action Lineage */}
          <div className="proof-section">
            <div className="proof-section-title">Approval to Action Conversion</div>
            <div className="proof-approval-grid">
              <div className="proof-kpi">
                <div className="proof-kpi-label">Approval Requests</div>
                <div className="proof-kpi-value">{dashboard.approvalConversion.totalApprovalRequests}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Granted</div>
                <div className="proof-kpi-value positive">{dashboard.approvalConversion.granted}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Denied</div>
                <div className="proof-kpi-value negative">{dashboard.approvalConversion.denied}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Executed After Approval</div>
                <div className="proof-kpi-value">{dashboard.approvalConversion.executedAfterApproval}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Execution Conversion</div>
                <div className="proof-kpi-value">{pct(dashboard.approvalConversion.executionConversionRate)}</div>
              </div>
              {dashboard.approvalConversion.meanApprovalLatency && (
                <div className="proof-kpi">
                  <div className="proof-kpi-label">Avg Approval Latency</div>
                  <div className="proof-kpi-value" style={{ fontSize: '1rem' }}>{dashboard.approvalConversion.meanApprovalLatency}</div>
                </div>
              )}
            </div>
          </div>

          {/* Execution Trends */}
          <div className="proof-section">
            <div className="proof-section-title">Execution Trends</div>
            <div className="proof-kpi-row" style={{ marginBottom: 14 }}>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Total Executions</div>
                <div className="proof-kpi-value">{dashboard.executionTrends.totalExecutions}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Successes</div>
                <div className="proof-kpi-value positive">{dashboard.executionTrends.successes}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Failures</div>
                <div className="proof-kpi-value negative">{dashboard.executionTrends.failures}</div>
              </div>
            </div>
            {dashboard.executionTrends.buckets.length > 0 && (
              <div className="proof-trend-bars">
                {dashboard.executionTrends.buckets.map((b, i) => {
                  const maxExec = Math.max(...dashboard.executionTrends.buckets.map(x => x.executions), 1);
                  const total = Math.max(b.executions, 1);
                  const height = (b.executions / maxExec) * 60;
                  const successH = (b.successes / total) * height;
                  const failH = height - successH;
                  return (
                    <div key={i} className="proof-trend-bar" title={`${b.successes}/${b.executions}`}>
                      <div className="proof-trend-bar-success" style={{ height: successH }} />
                      {failH > 0 && <div className="proof-trend-bar-failure" style={{ height: failH }} />}
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          {/* Override Rates */}
          <div className="proof-section">
            <div className="proof-section-title">Override & Reversal Rates</div>
            <div className="proof-kpi-row">
              <div className="proof-kpi">
                <div className="proof-kpi-label">Total Decisions</div>
                <div className="proof-kpi-value">{dashboard.overrideRates.totalDecisions}</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Overrides</div>
                <div className="proof-kpi-value">{dashboard.overrideRates.overrides}</div>
                <div className="proof-kpi-sub">{pct(dashboard.overrideRates.overrideRate)} of decisions</div>
              </div>
              <div className="proof-kpi">
                <div className="proof-kpi-label">Reversals</div>
                <div className="proof-kpi-value">{dashboard.overrideRates.reversals}</div>
                <div className="proof-kpi-sub">{pct(dashboard.overrideRates.reversalRate)} of decisions</div>
              </div>
            </div>
            {Object.keys(dashboard.overrideRates.overrideReasonDistribution).length > 0 && (
              <div className="proof-override-reasons" style={{ marginTop: 14 }}>
                {Object.entries(dashboard.overrideRates.overrideReasonDistribution).map(([reason, count]) => (
                  <span key={reason} className="proof-override-reason">{reason}: {count}</span>
                ))}
              </div>
            )}
          </div>

          {/* Trust Analytics */}
          {dashboard.trustAnalytics.byActionType.length > 0 && (
            <div className="proof-section">
              <div className="proof-section-title">Trust Analytics by Action Type</div>
              <table className="proof-trust-table">
                <thead>
                  <tr>
                    <th>Action Type</th>
                    <th>Decisions</th>
                    <th>Outcomes</th>
                    <th>Accuracy</th>
                    <th>Override Rate</th>
                    <th>Confidence</th>
                    <th>Variance</th>
                    <th>Grade</th>
                  </tr>
                </thead>
                <tbody>
                  {dashboard.trustAnalytics.byActionType.map((row: TrustByActionType) => (
                    <tr key={row.actionType}>
                      <td>{row.actionType}</td>
                      <td>{row.totalDecisions}</td>
                      <td>{row.withOutcomes}</td>
                      <td>{pct(row.accuracyRate)}</td>
                      <td>{pct(row.overrideRate)}</td>
                      <td>{pct(row.meanConfidence)}</td>
                      <td>{row.meanVariancePercent.toFixed(1)}%</td>
                      <td><span className={`proof-grade ${row.trustGrade.toLowerCase()}`}>{row.trustGrade}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}
