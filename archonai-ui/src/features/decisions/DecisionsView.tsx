import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './decisions.css';

interface Alternative {
  id: string;
  title: string;
  rationale: string;
  pros: string[];
  cons: string[];
  estimatedConfidence?: number;
  estimatedValue?: number;
}

interface DecisionLink {
  artifactType: string;
  artifactId: string;
  description: string;
  linkedAtUtc: string;
}

interface FinancialConsequence {
  id: string;
  decisionId: string;
  expectedRevenueImpactLow: number | null;
  expectedRevenueImpactHigh: number | null;
  expectedCostImpactLow: number | null;
  expectedCostImpactHigh: number | null;
  expectedMarginImpact: number | null;
  expectedCashTimingImpact: string | null;
  laborImpact: string | null;
  downsideRisk: number | null;
  upsidePotential: number | null;
  confidenceAdjustment: number | null;
  roiEstimateLow: number | null;
  roiEstimateHigh: number | null;
  breakEvenEstimate: string | null;
  assumptions: string[];
  notes: string | null;
  createdBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

interface LifecycleEvent {
  id: string;
  decisionId: string;
  eventType: string;
  actor: string;
  detail: string | null;
  occurredAtUtc: string;
}

interface Decision {
  id: string;
  tenantId: string;
  title: string;
  domain: string;
  objective: string;
  constraints: string[];
  assumptions: string[];
  alternatives: Alternative[];
  recommendedOptionId: string;
  confidence: number;
  reversibility: string;
  riskLevel: string;
  expectedValue: number | null;
  requiresApproval: boolean;
  linkedArtifacts: DecisionLink[];
  status: string;
  createdBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

const STATUS_FILTERS = ['All', 'Draft', 'Proposed', 'UnderReview', 'Approved', 'Rejected', 'Executing', 'Completed'];

function statusClass(status: string): string {
  return status.toLowerCase().replace(/\s/g, '');
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function pct(n: number): string {
  return `${Math.round(n * 100)}%`;
}

function fmtCurrency(n: number): string {
  const abs = Math.abs(n);
  const formatted = abs >= 1_000_000
    ? `$${(abs / 1_000_000).toFixed(1)}M`
    : abs >= 1_000
      ? `$${(abs / 1_000).toFixed(0)}K`
      : `$${abs.toLocaleString()}`;
  return n < 0 ? `-${formatted}` : formatted;
}

function fmtRange(low: number | null, high: number | null): string {
  if (low != null && high != null) return `${fmtCurrency(low)} – ${fmtCurrency(high)}`;
  if (low != null) return `${fmtCurrency(low)}+`;
  if (high != null) return `up to ${fmtCurrency(high)}`;
  return '—';
}

export function DecisionsView() {
  const [decisions, setDecisions] = useState<Decision[]>([]);
  const [selected, setSelected] = useState<Decision | null>(null);
  const [history, setHistory] = useState<LifecycleEvent[]>([]);
  const [finCon, setFinCon] = useState<FinancialConsequence | null>(null);
  const [trustEval, setTrustEval] = useState<{ actionScope: string; requestedTier: string; effectiveTier: string; allowed: boolean; disposition: string; reason: string | null } | null>(null);
  const [outcome, setOutcome] = useState<{
    id: string; decisionId: string; expectedOutcomeSummary: string | null;
    expectedValue: number | null; confidenceAtPrediction: number;
    actualOutcomeSummary: string | null; actualValue: number | null;
    valueVariance: number | null; variancePercent: number | null;
    direction: string; assessment: string; recalibrationSignal: string;
    rootCause: string | null; notes: string | null;
  } | null>(null);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('All');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params: Record<string, string | number> = {};
      if (filter !== 'All') params.status = filter;
      const data = await api.listDecisions(params) as Decision[];
      setDecisions(data);
    } catch {
      setDecisions([]);
    } finally {
      setLoading(false);
    }
  }, [filter]);

  useEffect(() => { load(); }, [load]);

  const openDetail = async (d: Decision) => {
    setSelected(d);
    try {
      const h = await api.getDecisionHistory(d.id) as LifecycleEvent[];
      setHistory(h);
    } catch {
      setHistory([]);
    }
    try {
      const fc = await api.getFinancialConsequence(d.id) as FinancialConsequence;
      setFinCon(fc);
    } catch {
      setFinCon(null);
    }
    try {
      const o = await api.getOutcome(d.id);
      setOutcome(o as typeof outcome);
    } catch {
      setOutcome(null);
    }
    try {
      const te = await api.evaluateTrustTier({
        actionScope: 'decision.execute',
        requestedTier: d.requiresApproval ? 'DraftApprovalRequired' : 'AutoExecuteReversible',
        confidence: d.confidence,
        reversible: d.reversibility !== 'Irreversible',
      });
      setTrustEval(te as typeof trustEval);
    } catch {
      setTrustEval(null);
    }
  };

  if (selected) {
    return (
      <div className="dec-detail">
        <header className="dec-detail-header">
          <div style={{ display: 'flex', alignItems: 'center', gap: 14, marginBottom: 12 }}>
            <button className="dec-back" onClick={() => setSelected(null)} style={{ background: 'none', border: 'none', cursor: 'pointer' }}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
            </button>
            <h1 className="dec-detail-title">{selected.title}</h1>
          </div>
          <div className="dec-detail-meta">
            <span className={`dec-badge ${statusClass(selected.status)}`}>{selected.status}</span>
            <span className={`dec-badge ${selected.riskLevel.toLowerCase()}`}>{selected.riskLevel} risk</span>
            <span className="dec-badge">{selected.reversibility}</span>
            <span className="dec-confidence">Confidence: {pct(selected.confidence)}</span>
            {selected.expectedValue != null && (
              <span className="dec-confidence">EV: ${selected.expectedValue.toLocaleString()}</span>
            )}
            {trustEval && (
              <span className={`dec-badge ${trustEval.allowed ? 'approved' : 'rejected'}`}>
                {trustEval.disposition.replace(/_/g, ' ')}
              </span>
            )}
          </div>
        </header>

        {selected.objective && (
          <div className="dec-section">
            <div className="dec-section-label">Objective</div>
            <p className="dec-objective">{selected.objective}</p>
          </div>
        )}

        {selected.constraints.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Constraints</div>
            <div className="dec-tags">{selected.constraints.map((c, i) => <span key={i} className="dec-tag">{c}</span>)}</div>
          </div>
        )}

        {selected.assumptions.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Assumptions</div>
            <div className="dec-tags">{selected.assumptions.map((a, i) => <span key={i} className="dec-tag">{a}</span>)}</div>
          </div>
        )}

        {selected.alternatives.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Alternatives Considered</div>
            <div className="dec-alt-list">
              {selected.alternatives.map((alt) => (
                <div key={alt.id} className={`dec-alt ${alt.id === selected.recommendedOptionId ? 'recommended' : ''}`}>
                  <div className="dec-alt-header">
                    <span className="dec-alt-title">{alt.title}</span>
                    {alt.id === selected.recommendedOptionId && <span className="dec-alt-rec">Recommended</span>}
                  </div>
                  <p className="dec-alt-rationale">{alt.rationale}</p>
                  {(alt.pros.length > 0 || alt.cons.length > 0) && (
                    <div className="dec-alt-pros-cons">
                      <div>
                        <div className="dec-alt-col-label pro">Pros</div>
                        {alt.pros.map((p, i) => <div key={i} className="dec-alt-item">+ {p}</div>)}
                      </div>
                      <div>
                        <div className="dec-alt-col-label con">Cons</div>
                        {alt.cons.map((c, i) => <div key={i} className="dec-alt-item">- {c}</div>)}
                      </div>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {finCon && (
          <div className="dec-section">
            <div className="dec-section-label">Financial Consequence</div>
            <div className="fin-con-grid">
              {(finCon.expectedRevenueImpactLow != null || finCon.expectedRevenueImpactHigh != null) && (
                <div className="fin-con-card">
                  <div className="fin-con-label">Revenue Impact</div>
                  <div className="fin-con-value fin-con-positive">
                    {fmtRange(finCon.expectedRevenueImpactLow, finCon.expectedRevenueImpactHigh)}
                  </div>
                </div>
              )}
              {(finCon.expectedCostImpactLow != null || finCon.expectedCostImpactHigh != null) && (
                <div className="fin-con-card">
                  <div className="fin-con-label">Cost Impact</div>
                  <div className="fin-con-value fin-con-negative">
                    {fmtRange(finCon.expectedCostImpactLow, finCon.expectedCostImpactHigh)}
                  </div>
                </div>
              )}
              {finCon.expectedMarginImpact != null && (
                <div className="fin-con-card">
                  <div className="fin-con-label">Margin Impact</div>
                  <div className={`fin-con-value ${finCon.expectedMarginImpact >= 0 ? 'fin-con-positive' : 'fin-con-negative'}`}>
                    {fmtCurrency(finCon.expectedMarginImpact)}
                  </div>
                </div>
              )}
              {finCon.downsideRisk != null && (
                <div className="fin-con-card">
                  <div className="fin-con-label">Downside Risk</div>
                  <div className="fin-con-value fin-con-negative">{fmtCurrency(finCon.downsideRisk)}</div>
                </div>
              )}
              {finCon.upsidePotential != null && (
                <div className="fin-con-card">
                  <div className="fin-con-label">Upside Potential</div>
                  <div className="fin-con-value fin-con-positive">{fmtCurrency(finCon.upsidePotential)}</div>
                </div>
              )}
              {(finCon.roiEstimateLow != null || finCon.roiEstimateHigh != null) && (
                <div className="fin-con-card">
                  <div className="fin-con-label">ROI Estimate</div>
                  <div className="fin-con-value">{fmtRange(finCon.roiEstimateLow, finCon.roiEstimateHigh)}</div>
                </div>
              )}
              {finCon.confidenceAdjustment != null && (
                <div className="fin-con-card">
                  <div className="fin-con-label">Confidence Adj.</div>
                  <div className="fin-con-value">{pct(finCon.confidenceAdjustment)}</div>
                </div>
              )}
              {finCon.breakEvenEstimate && (
                <div className="fin-con-card">
                  <div className="fin-con-label">Break-Even</div>
                  <div className="fin-con-value">{finCon.breakEvenEstimate}</div>
                </div>
              )}
            </div>
            {(finCon.expectedCashTimingImpact || finCon.laborImpact) && (
              <div className="fin-con-details">
                {finCon.expectedCashTimingImpact && (
                  <div className="fin-con-detail-row">
                    <span className="fin-con-detail-label">Cash Timing</span>
                    <span>{finCon.expectedCashTimingImpact}</span>
                  </div>
                )}
                {finCon.laborImpact && (
                  <div className="fin-con-detail-row">
                    <span className="fin-con-detail-label">Labor Impact</span>
                    <span>{finCon.laborImpact}</span>
                  </div>
                )}
              </div>
            )}
            {finCon.assumptions.length > 0 && (
              <div style={{ marginTop: 12 }}>
                <div className="fin-con-detail-label" style={{ marginBottom: 6 }}>Assumptions</div>
                <div className="dec-tags">{finCon.assumptions.map((a, i) => <span key={i} className="dec-tag">{a}</span>)}</div>
              </div>
            )}
            {finCon.notes && (
              <div style={{ marginTop: 12 }}>
                <div className="fin-con-detail-label" style={{ marginBottom: 6 }}>Notes</div>
                <p className="dec-objective">{finCon.notes}</p>
              </div>
            )}
          </div>
        )}

        {outcome && outcome.direction !== 'Pending' && (
          <div className="dec-section">
            <div className="dec-section-label">Outcome Comparison</div>
            <div className="outcome-grid">
              <div className="outcome-card">
                <div className="outcome-label">Expected Value</div>
                <div className="outcome-value">{outcome.expectedValue != null ? fmtCurrency(outcome.expectedValue) : '—'}</div>
              </div>
              <div className="outcome-card">
                <div className="outcome-label">Actual Value</div>
                <div className="outcome-value">{outcome.actualValue != null ? fmtCurrency(outcome.actualValue) : '—'}</div>
              </div>
              <div className="outcome-card">
                <div className="outcome-label">Variance</div>
                <div className={`outcome-value ${outcome.valueVariance != null && outcome.valueVariance >= 0 ? 'outcome-pos' : 'outcome-neg'}`}>
                  {outcome.valueVariance != null ? fmtCurrency(outcome.valueVariance) : '—'}
                  {outcome.variancePercent != null && <span className="outcome-pct"> ({outcome.variancePercent > 0 ? '+' : ''}{outcome.variancePercent.toFixed(1)}%)</span>}
                </div>
              </div>
              <div className="outcome-card">
                <div className="outcome-label">Confidence at Prediction</div>
                <div className="outcome-value">{pct(outcome.confidenceAtPrediction)}</div>
              </div>
            </div>
            <div className="outcome-indicators">
              <span className={`dec-badge ${outcome.direction.toLowerCase()}`}>{outcome.direction}</span>
              <span className={`dec-badge ${outcome.assessment.toLowerCase().replace(/\s/g, '')}`}>{outcome.assessment.replace(/([A-Z])/g, ' $1').trim()}</span>
              {outcome.recalibrationSignal !== 'None' && (
                <span className="dec-badge signal">{outcome.recalibrationSignal.replace(/([A-Z])/g, ' $1').trim()}</span>
              )}
            </div>
            {outcome.rootCause && (
              <div style={{ marginTop: 12 }}>
                <div className="fin-con-detail-label" style={{ marginBottom: 6 }}>Root Cause</div>
                <p className="dec-objective">{outcome.rootCause}</p>
              </div>
            )}
          </div>
        )}

        {selected.linkedArtifacts.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Linked Artifacts</div>
            <div className="dec-links">
              {selected.linkedArtifacts.map((link, i) => (
                <div key={i} className="dec-link-row">
                  <span className="dec-link-type">{link.artifactType}</span>
                  <span>{link.artifactId}</span>
                  <span style={{ color: 'var(--text-3)' }}>{link.description}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        {history.length > 0 && (
          <div className="dec-section">
            <div className="dec-section-label">Lifecycle History</div>
            <div className="dec-history">
              {history.map((evt) => (
                <div key={evt.id} className="dec-history-event">
                  <span className="dec-history-time">{fmtDate(evt.occurredAtUtc)}</span>
                  <span className="dec-history-type">{evt.eventType}</span>
                  <span className="dec-history-detail">{evt.detail}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="dec-view">
      <header className="dec-header">
        <div className="dec-header-left">
          <Link to="/" className="dec-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="dec-title">Decisions</h1>
            <p className="dec-subtitle">Structured business decisions with rationale, alternatives, and risk</p>
          </div>
        </div>
      </header>

      <div className="dec-filters">
        {STATUS_FILTERS.map((s) => (
          <button
            key={s}
            className={`dec-filter-btn ${filter === s ? 'active' : ''}`}
            onClick={() => setFilter(s)}
          >
            {s === 'UnderReview' ? 'Under Review' : s}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="dec-loading">Loading decisions...</div>
      ) : decisions.length === 0 ? (
        <div className="dec-list"><div className="dec-empty">No decisions found</div></div>
      ) : (
        <div className="dec-list">
          {decisions.map((d) => (
            <div key={d.id} className="dec-row" onClick={() => openDetail(d)}>
              <div>
                <p className="dec-row-title">{d.title}</p>
                <p className="dec-row-domain">{d.domain}</p>
              </div>
              <span className={`dec-badge ${statusClass(d.status)}`}>{d.status}</span>
              <span className={`dec-badge ${d.riskLevel.toLowerCase()}`}>{d.riskLevel}</span>
              <span className="dec-confidence">{pct(d.confidence)}</span>
              <span className="dec-confidence">{d.requiresApproval ? 'Yes' : 'No'}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
