import { useState, useEffect, useCallback } from 'react';
import { api } from '../../api/client';
import './simulation.css';

/* ── Types ─────────────────────────────────────────────────── */
interface PolicyOutcome {
  policyName: string;
  policyType: string;
  passed: boolean;
  explanation: string;
}
interface SimulatedDecision {
  title: string;
  domain: string;
  riskLevel: string;
  reversibility: string;
  confidence: number;
  expectedValue: number | null;
  wouldRequireApproval: boolean;
  proposedStatus: string;
}
interface SimulatedApproval {
  required: boolean;
  requiredApproverRole: string | null;
  requiresSeparationOfDuties: boolean;
  matchedPolicyActionType: string | null;
  explanation: string;
}
interface SimulatedEconomicEffect {
  revenueImpactLow: number | null;
  revenueImpactHigh: number | null;
  costImpactLow: number | null;
  costImpactHigh: number | null;
  netImpactLow: number | null;
  netImpactHigh: number | null;
  downsideRisk: number | null;
  upsidePotential: number | null;
  summary: string | null;
}
interface SimulatedWorkflowStep {
  stepId: string;
  name: string;
  subsystem: string;
  projectedOutcome: string;
}
interface SimulatedWorkflowPreview {
  workflowType: string;
  displayName: string;
  totalSteps: number;
  steps: SimulatedWorkflowStep[];
}
interface TrustTierOutcome {
  allowed: boolean;
  disposition: string;
  effectiveTier: string;
  requestedTier: string;
  reason: string | null;
}
interface SimulationResult {
  id: string;
  tenantId: string;
  actionType: string;
  title: string;
  verdict: string;
  decision: SimulatedDecision | null;
  trustTierOutcome: TrustTierOutcome | null;
  approvalRequirement: SimulatedApproval | null;
  policyOutcomes: PolicyOutcome[];
  economicEffect: SimulatedEconomicEffect | null;
  workflowPreview: SimulatedWorkflowPreview | null;
  reasons: string[];
  simulatedBy: string;
  simulatedAtUtc: string;
}
interface SimulationSummary {
  id: string;
  actionType: string;
  title: string;
  verdict: string;
  simulatedBy: string;
  simulatedAtUtc: string;
}

/* ── Helpers ───────────────────────────────────────────────── */
function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}
function fmtCurrency(v: number | null | undefined): string {
  if (v == null) return '—';
  return v.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
}

function verdictLabel(v: string): string {
  const map: Record<string, string> = {
    Allowed: 'Allowed',
    RequiresApproval: 'Requires Approval',
    Blocked: 'Blocked',
    RecommendOnly: 'Recommend Only',
    ObserveOnly: 'Observe Only',
  };
  return map[v] ?? v;
}

/* ── Component ─────────────────────────────────────────────── */
export function PolicySimulationView() {
  const [tab, setTab] = useState<'simulate' | 'history'>('simulate');
  const [history, setHistory] = useState<SimulationSummary[]>([]);
  const [result, setResult] = useState<SimulationResult | null>(null);
  const [loading, setLoading] = useState(false);

  // Form state
  const [actionType, setActionType] = useState('');
  const [actionScope, setActionScope] = useState('');
  const [title, setTitle] = useState('');
  const [domain, setDomain] = useState('');
  const [riskLevel, setRiskLevel] = useState('Medium');
  const [reversibility, setReversibility] = useState('PartiallyReversible');
  const [confidence, setConfidence] = useState('0.7');
  const [expectedValue, setExpectedValue] = useState('');
  const [requestedTier, setRequestedTier] = useState('DraftApprovalRequired');
  const [workflowType, setWorkflowType] = useState('');
  const [revLow, setRevLow] = useState('');
  const [revHigh, setRevHigh] = useState('');
  const [costLow, setCostLow] = useState('');
  const [costHigh, setCostHigh] = useState('');
  const [downsideRisk, setDownsideRisk] = useState('');
  const [upsidePotential, setUpsidePotential] = useState('');
  const [workflowCatalog, setWorkflowCatalog] = useState<{ workflowType: string; displayName: string }[]>([]);

  useEffect(() => {
    (async () => {
      try {
        const cat = await api.getHeroWorkflowCatalog() as { workflowType: string; displayName: string }[];
        setWorkflowCatalog(cat);
      } catch { /* catalog not available, fall back to empty */ }
    })();
  }, []);

  const loadHistory = useCallback(async () => {
    try {
      const data = await api.listSimulations(50) as SimulationSummary[];
      setHistory(data);
    } catch { /* swallow */ }
  }, []);

  useEffect(() => {
    if (tab === 'history') loadHistory();
  }, [tab, loadHistory]);

  const runSimulation = async () => {
    if (!actionType.trim() || !title.trim()) return;
    setLoading(true);
    try {
      const body: Record<string, unknown> = {
        actionType: actionType.trim(),
        title: title.trim(),
      };
      if (actionScope) body.actionScope = actionScope;
      if (domain) body.domain = domain;
      if (riskLevel) body.riskLevel = riskLevel;
      if (reversibility) body.reversibility = reversibility;
      if (confidence) body.confidence = parseFloat(confidence);
      if (expectedValue) body.expectedValue = parseFloat(expectedValue);
      if (requestedTier) body.requestedTier = requestedTier;
      if (workflowType) body.workflowType = workflowType;
      if (revLow) body.revenueImpactLow = parseFloat(revLow);
      if (revHigh) body.revenueImpactHigh = parseFloat(revHigh);
      if (costLow) body.costImpactLow = parseFloat(costLow);
      if (costHigh) body.costImpactHigh = parseFloat(costHigh);
      if (downsideRisk) body.downsideRisk = parseFloat(downsideRisk);
      if (upsidePotential) body.upsidePotential = parseFloat(upsidePotential);

      const res = await api.runSimulation(body as Parameters<typeof api.runSimulation>[0]) as SimulationResult;
      setResult(res);
    } catch (err) {
      console.error('Simulation failed', err);
    } finally {
      setLoading(false);
    }
  };

  const loadDetail = async (id: string) => {
    setLoading(true);
    try {
      const res = await api.getSimulation(id) as SimulationResult;
      setResult(res);
      setTab('simulate');
    } catch { /* swallow */ }
    finally { setLoading(false); }
  };

  return (
    <div className="sim-view">
      <header className="sim-header">
        <h1 className="sim-title">Policy Simulation</h1>
        <p className="sim-subtitle">
          Dry-run mode — preview what ArchonAI would do without executing any actions
        </p>
      </header>

      <div className="sim-tabs">
        <button className={`sim-tab${tab === 'simulate' ? ' sim-tab--active' : ''}`} onClick={() => setTab('simulate')}>
          Simulate
        </button>
        <button className={`sim-tab${tab === 'history' ? ' sim-tab--active' : ''}`} onClick={() => setTab('history')}>
          History
        </button>
      </div>

      {tab === 'simulate' && (
        <>
          {/* ── Form ────────────────────────────────────── */}
          <div className="sim-form">
            <div className="sim-field">
              <label className="sim-label">Action Type *</label>
              <input className="sim-input" placeholder="e.g. vendor-selection" value={actionType} onChange={e => setActionType(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Title *</label>
              <input className="sim-input" placeholder="e.g. Select cloud provider" value={title} onChange={e => setTitle(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Action Scope</label>
              <input className="sim-input" placeholder="e.g. vendor-selection" value={actionScope} onChange={e => setActionScope(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Domain</label>
              <input className="sim-input" placeholder="e.g. procurement" value={domain} onChange={e => setDomain(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Risk Level</label>
              <select className="sim-select" value={riskLevel} onChange={e => setRiskLevel(e.target.value)}>
                <option value="Low">Low</option>
                <option value="Medium">Medium</option>
                <option value="High">High</option>
                <option value="Critical">Critical</option>
              </select>
            </div>
            <div className="sim-field">
              <label className="sim-label">Reversibility</label>
              <select className="sim-select" value={reversibility} onChange={e => setReversibility(e.target.value)}>
                <option value="FullyReversible">Fully Reversible</option>
                <option value="PartiallyReversible">Partially Reversible</option>
                <option value="Irreversible">Irreversible</option>
              </select>
            </div>
            <div className="sim-field">
              <label className="sim-label">Confidence (0–1)</label>
              <input className="sim-input" type="number" step="0.1" min="0" max="1" placeholder="0.7" value={confidence} onChange={e => setConfidence(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Expected Value ($)</label>
              <input className="sim-input" type="number" placeholder="e.g. 500000" value={expectedValue} onChange={e => setExpectedValue(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Requested Trust Tier</label>
              <select className="sim-select" value={requestedTier} onChange={e => setRequestedTier(e.target.value)}>
                <option value="ObserveOnly">Observe Only</option>
                <option value="RecommendOnly">Recommend Only</option>
                <option value="DraftApprovalRequired">Draft — Approval Required</option>
                <option value="AutoExecuteReversible">Auto-Execute (Reversible)</option>
                <option value="AutoExecuteFull">Auto-Execute (Full)</option>
              </select>
            </div>
            <div className="sim-field">
              <label className="sim-label">Workflow Type</label>
              <select className="sim-select" value={workflowType} onChange={e => setWorkflowType(e.target.value)}>
                <option value="">— None —</option>
                {workflowCatalog.map(wf => (
                  <option key={wf.workflowType} value={wf.workflowType}>{wf.displayName}</option>
                ))}
              </select>
            </div>
            <div className="sim-field">
              <label className="sim-label">Revenue Impact Low ($)</label>
              <input className="sim-input" type="number" placeholder="" value={revLow} onChange={e => setRevLow(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Revenue Impact High ($)</label>
              <input className="sim-input" type="number" placeholder="" value={revHigh} onChange={e => setRevHigh(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Cost Impact Low ($)</label>
              <input className="sim-input" type="number" placeholder="" value={costLow} onChange={e => setCostLow(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Cost Impact High ($)</label>
              <input className="sim-input" type="number" placeholder="" value={costHigh} onChange={e => setCostHigh(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Downside Risk ($)</label>
              <input className="sim-input" type="number" placeholder="" value={downsideRisk} onChange={e => setDownsideRisk(e.target.value)} />
            </div>
            <div className="sim-field">
              <label className="sim-label">Upside Potential ($)</label>
              <input className="sim-input" type="number" placeholder="" value={upsidePotential} onChange={e => setUpsidePotential(e.target.value)} />
            </div>
            <div className="sim-form-full">
              <button className="sim-run-btn" onClick={runSimulation} disabled={loading || !actionType.trim() || !title.trim()}>
                {loading ? 'Simulating...' : 'Run Dry-Run Simulation'}
              </button>
            </div>
          </div>

          {/* ── Result ──────────────────────────────────── */}
          {result && (
            <>
              <div className={`sim-verdict sim-verdict--${result.verdict}`}>
                <div>
                  <div className="sim-verdict-label">Simulation Verdict</div>
                  <div className="sim-verdict-value">{verdictLabel(result.verdict)}</div>
                </div>
                <div style={{ marginLeft: 'auto', textAlign: 'right' }}>
                  <div className="sim-verdict-label">Simulated</div>
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '11px', color: 'var(--text-2)' }}>
                    {fmtDate(result.simulatedAtUtc)}
                  </div>
                </div>
              </div>

              {/* Policy Outcomes */}
              <div className="sim-section">
                <h3 className="sim-section-title">Policy Outcomes</h3>
                {result.policyOutcomes.map((po, i) => (
                  <div key={i} className="sim-policy-row">
                    <span className="sim-policy-icon">{po.passed ? '\u2705' : '\u274C'}</span>
                    <span className="sim-policy-name">{po.policyName}</span>
                    <span className="sim-policy-explain">{po.explanation}</span>
                  </div>
                ))}
              </div>

              {/* Decision Preview */}
              {result.decision && (
                <div className="sim-section">
                  <h3 className="sim-section-title">Simulated Decision</h3>
                  <div className="sim-kv">
                    <span className="sim-kv-key">Title</span><span className="sim-kv-val">{result.decision.title}</span>
                    <span className="sim-kv-key">Domain</span><span className="sim-kv-val">{result.decision.domain}</span>
                    <span className="sim-kv-key">Risk Level</span><span className="sim-kv-val">{result.decision.riskLevel}</span>
                    <span className="sim-kv-key">Reversibility</span><span className="sim-kv-val">{result.decision.reversibility}</span>
                    <span className="sim-kv-key">Confidence</span><span className="sim-kv-val">{(result.decision.confidence * 100).toFixed(0)}%</span>
                    <span className="sim-kv-key">Expected Value</span><span className="sim-kv-val">{fmtCurrency(result.decision.expectedValue)}</span>
                    <span className="sim-kv-key">Requires Approval</span><span className="sim-kv-val">{result.decision.wouldRequireApproval ? 'Yes' : 'No'}</span>
                    <span className="sim-kv-key">Proposed Status</span><span className="sim-kv-val">{result.decision.proposedStatus}</span>
                  </div>
                </div>
              )}

              {/* Trust Tier */}
              {result.trustTierOutcome && (
                <div className="sim-section">
                  <h3 className="sim-section-title">Trust Tier Evaluation</h3>
                  <div className="sim-kv">
                    <span className="sim-kv-key">Allowed</span><span className="sim-kv-val">{result.trustTierOutcome.allowed ? 'Yes' : 'No'}</span>
                    <span className="sim-kv-key">Disposition</span><span className="sim-kv-val">{result.trustTierOutcome.disposition}</span>
                    <span className="sim-kv-key">Effective Tier</span><span className="sim-kv-val">{result.trustTierOutcome.effectiveTier}</span>
                    <span className="sim-kv-key">Requested Tier</span><span className="sim-kv-val">{result.trustTierOutcome.requestedTier}</span>
                    {result.trustTierOutcome.reason && (
                      <><span className="sim-kv-key">Reason</span><span className="sim-kv-val">{result.trustTierOutcome.reason}</span></>
                    )}
                  </div>
                </div>
              )}

              {/* Approval Requirement */}
              {result.approvalRequirement && (
                <div className="sim-section">
                  <h3 className="sim-section-title">Approval Requirement</h3>
                  <div className="sim-kv">
                    <span className="sim-kv-key">Required</span><span className="sim-kv-val">{result.approvalRequirement.required ? 'Yes' : 'No'}</span>
                    {result.approvalRequirement.requiredApproverRole && (
                      <><span className="sim-kv-key">Approver Role</span><span className="sim-kv-val">{result.approvalRequirement.requiredApproverRole}</span></>
                    )}
                    <span className="sim-kv-key">Separation of Duties</span><span className="sim-kv-val">{result.approvalRequirement.requiresSeparationOfDuties ? 'Yes' : 'No'}</span>
                    {result.approvalRequirement.matchedPolicyActionType && (
                      <><span className="sim-kv-key">Matched Policy</span><span className="sim-kv-val">{result.approvalRequirement.matchedPolicyActionType}</span></>
                    )}
                    <span className="sim-kv-key">Explanation</span><span className="sim-kv-val">{result.approvalRequirement.explanation}</span>
                  </div>
                </div>
              )}

              {/* Economic Effect */}
              {result.economicEffect && (
                <div className="sim-section">
                  <h3 className="sim-section-title">Projected Economic Impact</h3>
                  <div className="sim-kv">
                    <span className="sim-kv-key">Revenue Impact</span>
                    <span className="sim-kv-val">{fmtCurrency(result.economicEffect.revenueImpactLow)} — {fmtCurrency(result.economicEffect.revenueImpactHigh)}</span>
                    <span className="sim-kv-key">Cost Impact</span>
                    <span className="sim-kv-val">{fmtCurrency(result.economicEffect.costImpactLow)} — {fmtCurrency(result.economicEffect.costImpactHigh)}</span>
                    <span className="sim-kv-key">Net Impact</span>
                    <span className="sim-kv-val">{fmtCurrency(result.economicEffect.netImpactLow)} — {fmtCurrency(result.economicEffect.netImpactHigh)}</span>
                    {result.economicEffect.downsideRisk != null && (
                      <><span className="sim-kv-key">Downside Risk</span><span className="sim-kv-val">{fmtCurrency(result.economicEffect.downsideRisk)}</span></>
                    )}
                    {result.economicEffect.upsidePotential != null && (
                      <><span className="sim-kv-key">Upside Potential</span><span className="sim-kv-val">{fmtCurrency(result.economicEffect.upsidePotential)}</span></>
                    )}
                  </div>
                </div>
              )}

              {/* Workflow Preview */}
              {result.workflowPreview && (
                <div className="sim-section">
                  <h3 className="sim-section-title">
                    Workflow Preview — {result.workflowPreview.displayName} ({result.workflowPreview.totalSteps} steps)
                  </h3>
                  {result.workflowPreview.steps.map((s, i) => (
                    <div key={i} className="sim-wf-step">
                      <span className="sim-wf-step-id">{s.stepId}</span>
                      <span className="sim-wf-step-name">{s.name}</span>
                      <span className="sim-wf-step-outcome">{s.projectedOutcome}</span>
                    </div>
                  ))}
                </div>
              )}

              {/* Reasons / Rationale */}
              {result.reasons.length > 0 && (
                <div className="sim-section">
                  <h3 className="sim-section-title">Rationale</h3>
                  <ul className="sim-reasons">
                    {result.reasons.map((r, i) => <li key={i}>{r}</li>)}
                  </ul>
                </div>
              )}
            </>
          )}
        </>
      )}

      {tab === 'history' && (
        <>
          {history.length === 0 ? (
            <div className="sim-empty">No simulation history yet. Run a dry-run simulation to see results here.</div>
          ) : (
            <div className="sim-history-list">
              {history.map(h => (
                <div key={h.id} className="sim-history-row" onClick={() => loadDetail(h.id)}>
                  <span className="sim-history-action">{h.actionType}</span>
                  <span className="sim-history-title">{h.title}</span>
                  <span className={`sim-verdict-pill sim-verdict-pill--${h.verdict.toLowerCase()}`}>{verdictLabel(h.verdict)}</span>
                  <span className="sim-history-ts">{fmtDate(h.simulatedAtUtc)}</span>
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}
