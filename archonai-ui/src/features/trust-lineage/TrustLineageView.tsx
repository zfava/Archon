import { useState, useEffect, useCallback } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import './trust-lineage.css';

/* eslint-disable @typescript-eslint/no-explicit-any */

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function fmtCurrency(n: number): string {
  const abs = Math.abs(n);
  const f = abs >= 1_000_000 ? `$${(abs / 1_000_000).toFixed(1)}M`
    : abs >= 1_000 ? `$${(abs / 1_000).toFixed(0)}K`
    : `$${abs.toLocaleString()}`;
  return n < 0 ? `-${f}` : f;
}

function pct(n: number): string { return `${Math.round(n * 100)}%`; }

function reversibilityClass(level: string): string {
  return level === 'Reversible' ? 'safe' : level === 'Compensatable' ? 'caution' : 'danger';
}

function LineageDetail({ decisionId }: { decisionId: string }) {
  const [lineage, setLineage] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    api.getTrustLineage(decisionId)
      .then(setLineage)
      .catch((e: any) => setError(e?.message ?? 'Failed to load lineage'))
      .finally(() => setLoading(false));
  }, [decisionId]);

  if (loading) return <div className="tl-loading">Loading trust lineage...</div>;
  if (error) return <div className="tl-error">{error}</div>;
  if (!lineage) return <div className="tl-empty">No lineage data found</div>;

  const d = lineage.decision;
  const ls = lineage.lineageSummary;

  return (
    <div>
      {/* Decision Header */}
      <div className="tl-decision-header">
        <h2 className="tl-decision-title">{d.title}</h2>
        <div className="tl-decision-meta">
          <span className="tl-badge domain">{d.domain}</span>
          <span className={`tl-badge risk-${d.riskLevel.toLowerCase()}`}>{d.riskLevel} Risk</span>
          <span className={`tl-badge ${reversibilityClass(d.reversibility)}`}>{d.reversibility}</span>
          <span className="tl-badge">{d.status}</span>
          {d.confidence != null && <span className="tl-badge">Conf: {pct(d.confidence)}</span>}
        </div>
      </div>

      {/* Lineage Completeness Indicators */}
      <div className="tl-completeness">
        <div className={`tl-phase ${ls.hasApproval ? 'complete' : 'missing'}`}>
          <div className="tl-phase-icon">{ls.hasApproval ? '\u2713' : '\u2014'}</div>
          <div className="tl-phase-label">Approval</div>
        </div>
        <div className="tl-phase-connector" />
        <div className={`tl-phase ${ls.hasExecution ? 'complete' : 'missing'}`}>
          <div className="tl-phase-icon">{ls.hasExecution ? '\u2713' : '\u2014'}</div>
          <div className="tl-phase-label">Execution</div>
        </div>
        <div className="tl-phase-connector" />
        <div className={`tl-phase ${ls.hasOutcome ? 'complete' : 'missing'}`}>
          <div className="tl-phase-icon">{ls.hasOutcome ? '\u2713' : '\u2014'}</div>
          <div className="tl-phase-label">Outcome</div>
        </div>
        <div className="tl-phase-connector" />
        <div className={`tl-phase ${ls.hasProofTrail ? 'complete' : 'missing'}`}>
          <div className="tl-phase-icon">{ls.hasProofTrail ? '\u2713' : '\u2014'}</div>
          <div className="tl-phase-label">Proof Trail</div>
        </div>
      </div>

      {/* Safety Indicators */}
      <div className="tl-safety-row">
        <div className={`tl-safety-indicator ${ls.allActionsReversible ? 'safe' : 'caution'}`}>
          {ls.allActionsReversible ? 'All actions reversible' : 'Contains irreversible actions'}
        </div>
        {ls.anyRollbackAttempted && (
          <div className="tl-safety-indicator caution">Rollback attempted</div>
        )}
        <div className={`tl-safety-indicator ${ls.varianceWithinThreshold ? 'safe' : 'caution'}`}>
          {ls.varianceWithinThreshold ? 'Variance within threshold' : 'Variance exceeds 20%'}
        </div>
      </div>

      {/* Approval Gates */}
      {lineage.approvalGates.length > 0 && (
        <div className="tl-section">
          <div className="tl-section-title">Approval Gates</div>
          {lineage.approvalGates.map((gate: any) => (
            <div key={gate.id} className="tl-gate-card">
              <div className="tl-gate-header">
                <span className={`tl-badge ${gate.status === 'Approved' ? 'safe' : gate.status === 'Denied' ? 'danger' : 'pending'}`}>
                  {gate.status}
                </span>
                <span className="tl-gate-type">{gate.actionType}</span>
                <span className="tl-gate-time">{fmtDate(gate.requestedAtUtc)}</span>
              </div>
              <div className="tl-gate-body">
                <div><span className="tl-label">Requested by:</span> {gate.requestedBy}</div>
                <div><span className="tl-label">Justification:</span> {gate.justification}</div>
                {gate.reviewedBy && (
                  <div><span className="tl-label">Reviewed by:</span> {gate.reviewedBy}
                    {gate.reviewedAtUtc && <span className="tl-dim"> at {fmtDate(gate.reviewedAtUtc)}</span>}
                  </div>
                )}
                {gate.reviewNotes && <div><span className="tl-label">Notes:</span> {gate.reviewNotes}</div>}
                {gate.executionStatus !== 'NotExecuted' && (
                  <div>
                    <span className="tl-label">Execution:</span>{' '}
                    <span className={gate.executionStatus === 'Succeeded' ? 'tl-success' : 'tl-failure'}>
                      {gate.executionStatus}
                    </span>
                    {gate.executionError && <span className="tl-dim"> — {gate.executionError}</span>}
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Governed Actions */}
      {lineage.governedActions.length > 0 && (
        <div className="tl-section">
          <div className="tl-section-title">Governed Actions</div>
          {lineage.governedActions.map((action: any) => (
            <div key={action.id} className="tl-action-card">
              <div className="tl-action-header">
                <span className="tl-action-type">{action.actionType}</span>
                <span className={`tl-badge ${reversibilityClass(action.safety.reversibility)}`}>
                  {action.safety.reversibility}
                </span>
                {action.safety.rollbackSupported && (
                  <span className="tl-badge safe">Rollback: {action.safety.rollbackStrategy}</span>
                )}
              </div>
              <div className="tl-action-body">
                <div>{action.description}</div>
                <div className="tl-action-meta">
                  <span>By {action.executedBy}</span>
                  <span>{fmtDate(action.executedAtUtc)}</span>
                  <span>Status: {action.status}</span>
                </div>
                <div className="tl-safety-summary">{action.safety.safetySummary}</div>
                {action.safety.rollbackWindow && (
                  <div className="tl-dim">Rollback window: {action.safety.rollbackWindow} minutes</div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Predicted vs Actual Outcome */}
      {lineage.outcome && (
        <div className="tl-section">
          <div className="tl-section-title">Predicted vs Actual Outcome</div>
          <div className="tl-outcome-grid">
            <div className="tl-outcome-cell">
              <div className="tl-outcome-label">Expected</div>
              <div className="tl-outcome-value">
                {lineage.outcome.expectedValue != null ? fmtCurrency(lineage.outcome.expectedValue) : '--'}
              </div>
              {lineage.outcome.expectedOutcomeSummary && <div className="tl-dim">{lineage.outcome.expectedOutcomeSummary}</div>}
            </div>
            <div className="tl-outcome-cell">
              <div className="tl-outcome-label">Actual</div>
              <div className="tl-outcome-value">
                {lineage.outcome.actualValue != null ? fmtCurrency(lineage.outcome.actualValue) : '--'}
              </div>
              {lineage.outcome.actualOutcomeSummary && <div className="tl-dim">{lineage.outcome.actualOutcomeSummary}</div>}
            </div>
            <div className="tl-outcome-cell">
              <div className="tl-outcome-label">Variance</div>
              <div className={`tl-outcome-value ${lineage.outcome.valueVariance >= 0 ? 'tl-success' : 'tl-failure'}`}>
                {lineage.outcome.valueVariance != null ? fmtCurrency(lineage.outcome.valueVariance) : '--'}
                {lineage.outcome.variancePercent != null && (
                  <span className="tl-dim"> ({lineage.outcome.variancePercent > 0 ? '+' : ''}{lineage.outcome.variancePercent.toFixed(1)}%)</span>
                )}
              </div>
            </div>
            <div className="tl-outcome-cell">
              <div className="tl-outcome-label">Calibration</div>
              <div className="tl-outcome-value">
                {lineage.outcome.assessment}
              </div>
              <div className="tl-dim">Conf at prediction: {pct(lineage.outcome.confidenceAtPrediction)}</div>
            </div>
          </div>
        </div>
      )}

      {/* Proof Event Timeline */}
      {lineage.proofTimeline && lineage.proofTimeline.events.length > 0 && (
        <div className="tl-section">
          <div className="tl-section-title">Proof Event Trail ({lineage.proofTimeline.totalEvents} events)</div>
          <div className="tl-proof-timeline">
            {lineage.proofTimeline.events.map((evt: any, i: number) => (
              <div key={i} className="tl-proof-event">
                <div className="tl-proof-time">{fmtDate(evt.occurredAtUtc)}</div>
                <div className="tl-proof-type">{evt.eventType.replace(/([A-Z])/g, ' $1').trim()}</div>
                {evt.detail && <div className="tl-proof-detail">{evt.detail}</div>}
                <div className="tl-proof-values">
                  {evt.actor && <span>Actor: {evt.actor}</span>}
                  {evt.isSuccess != null && <span>{evt.isSuccess ? 'Success' : 'Failed'}</span>}
                  {evt.overrideReason && <span>Override: {evt.overrideReason}</span>}
                  {evt.economicImpact != null && <span>Impact: {fmtCurrency(evt.economicImpact)}</span>}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Cross-links */}
      <div className="tl-cross-links">
        <Link to="/proof-analytics" className="tl-cross-link">Proof Analytics</Link>
        <Link to="/action-safety" className="tl-cross-link">Action Safety</Link>
        <Link to="/trust-tiers" className="tl-cross-link">Trust Tiers</Link>
        <Link to="/inspection" className="tl-cross-link">Inspect Decision</Link>
      </div>
    </div>
  );
}

function PostureSummary() {
  const [posture, setPosture] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.getTrustPosture()
      .then(setPosture)
      .catch((e: any) => setError(e?.message ?? 'Failed'))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="tl-loading">Loading trust posture...</div>;
  if (error) return <div className="tl-error">{error}</div>;
  if (!posture) return <div className="tl-empty">No posture data</div>;

  const g = posture.governance;
  const s = posture.safety;

  return (
    <div className="tl-posture">
      <div className="tl-posture-section">
        <div className="tl-section-title">Governance</div>
        <div className="tl-kpi-row">
          <div className="tl-kpi"><div className="tl-kpi-label">Active Policies</div><div className="tl-kpi-value">{g.activePolicies}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Pending Approvals</div><div className="tl-kpi-value">{g.pendingApprovals}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Approval Rate</div><div className="tl-kpi-value">{pct(g.recentApprovals.approvalRate)}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">SoD Enforced</div><div className={`tl-kpi-value ${g.separationOfDutiesEnforced ? 'tl-success' : ''}`}>{g.separationOfDutiesEnforced ? 'Yes' : 'No'}</div></div>
        </div>
      </div>

      <div className="tl-posture-section">
        <div className="tl-section-title">Safety & Reversibility</div>
        <div className="tl-kpi-row">
          <div className="tl-kpi"><div className="tl-kpi-label">Total Actions</div><div className="tl-kpi-value">{s.totalActions}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Reversible</div><div className="tl-kpi-value tl-success">{s.reversible}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Compensatable</div><div className="tl-kpi-value">{s.compensatable}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Irreversible</div><div className={`tl-kpi-value ${s.irreversible > 0 ? 'tl-failure' : ''}`}>{s.irreversible}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Reversibility Rate</div><div className="tl-kpi-value">{pct(s.reversibilityRate)}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Rollback Success</div><div className="tl-kpi-value">{pct(s.rollbackSuccessRate)}</div></div>
        </div>
      </div>

      <div className="tl-posture-section">
        <div className="tl-section-title">Trust Tier Coverage</div>
        <div className="tl-kpi-row">
          <div className="tl-kpi"><div className="tl-kpi-label">Tier Policies</div><div className="tl-kpi-value">{posture.trustTiers.enabledPolicies}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Actions Covered</div><div className="tl-kpi-value">{posture.trustTiers.actionsCovered}</div></div>
          <div className="tl-kpi"><div className="tl-kpi-label">Require Reversible</div><div className="tl-kpi-value">{posture.trustTiers.requiresReversible}</div></div>
        </div>
      </div>
    </div>
  );
}

export function TrustLineageView() {
  const [searchParams] = useSearchParams();
  const decisionIdParam = searchParams.get('decisionId');
  const [decisionId, setDecisionId] = useState<string>(decisionIdParam ?? '');
  const [activeDecisionId, setActiveDecisionId] = useState<string | null>(decisionIdParam);
  const [tab, setTab] = useState<'lineage' | 'posture'>(decisionIdParam ? 'lineage' : 'posture');

  const handleLookup = useCallback(() => {
    if (decisionId.trim()) {
      setActiveDecisionId(decisionId.trim());
      setTab('lineage');
    }
  }, [decisionId]);

  return (
    <div className="tl-view">
      <header className="tl-header">
        <div className="tl-header-left">
          <Link to="/" className="tl-back">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M19 12H5M12 19l-7-7 7-7" /></svg>
          </Link>
          <div>
            <h1 className="tl-title">Trust Lineage</h1>
            <p className="tl-subtitle">Every decision traced from approval through execution to measured outcome</p>
          </div>
        </div>
      </header>

      <div className="tl-tabs">
        <button className={`tl-tab ${tab === 'posture' ? 'active' : ''}`} onClick={() => setTab('posture')}>Trust Posture</button>
        <button className={`tl-tab ${tab === 'lineage' ? 'active' : ''}`} onClick={() => setTab('lineage')}>Decision Lineage</button>
      </div>

      {tab === 'lineage' && (
        <div className="tl-lookup">
          <input
            className="tl-lookup-input"
            type="text"
            placeholder="Enter decision ID..."
            value={decisionId}
            onChange={e => setDecisionId(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && handleLookup()}
          />
          <button className="tl-lookup-btn" onClick={handleLookup}>Trace Lineage</button>
        </div>
      )}

      {tab === 'posture' && <PostureSummary />}
      {tab === 'lineage' && activeDecisionId && <LineageDetail decisionId={activeDecisionId} />}
      {tab === 'lineage' && !activeDecisionId && (
        <div className="tl-empty">Enter a decision ID above to trace its full trust lineage</div>
      )}
    </div>
  );
}
