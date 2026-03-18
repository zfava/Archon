import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './executive-command.css';

interface ExceptionHeadline {
  id: string;
  severity: string;
  category: string;
  title: string;
  priorityScore: number;
  economicImpactEstimate: number;
  recommendedActionType: string | null;
}
interface ExceptionBrief {
  totalOpen: number;
  critical: number;
  high: number;
  totalEconomicExposure: number;
  topExceptions: ExceptionHeadline[];
}
interface ApprovalHeadline {
  id: string;
  actionType: string;
  requestedBy: string;
  justification: string;
  requestedAtUtc: string;
}
interface ApprovalBrief {
  pendingCount: number;
  pendingApprovals: ApprovalHeadline[];
}
interface CalibrationBrief {
  totalOutcomes: number;
  underperformed: number;
  hitRate: number;
  meanVariancePercent: number;
  signalDistribution: Record<string, number>;
}
interface BottleneckHeadline {
  id: string;
  severity: string;
  description: string;
  detectedAtUtc: string;
}
interface OperationalBrief {
  entityCounts: Record<string, number>;
  activeBottlenecks: number;
  warningKpis: number;
  totalDependencies: number;
  topBottlenecks: BottleneckHeadline[];
}
interface TrustTierBrief {
  totalPolicies: number;
  tierMap: Record<string, string>;
}
interface ScenarioHeadline {
  id: string;
  title: string;
  type: string;
  status: string;
  assumptionCount: number;
  effectCount: number;
  updatedAtUtc: string;
}
interface ScenarioBrief {
  totalActive: number;
  totalCompared: number;
  recentScenarios: ScenarioHeadline[];
}
interface EconomicBrief {
  exceptionExposure: number;
  decisionsPendingApproval: number;
  outcomesDrifting: number;
  activeBottlenecks: number;
}
interface ExecSummary {
  tenantId: string;
  exceptionBrief: ExceptionBrief;
  approvalBrief: ApprovalBrief;
  calibrationBrief: CalibrationBrief;
  operationalBrief: OperationalBrief;
  trustBrief: TrustTierBrief;
  scenarioBrief: ScenarioBrief;
  economicBrief: EconomicBrief;
  generatedAtUtc: string;
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function fmtCurrency(n: number): string {
  if (n >= 1_000_000) return `$${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `$${(n / 1_000).toFixed(0)}K`;
  return `$${n.toFixed(0)}`;
}

function sevClass(s: string): string { return `exec-sev-${s.toLowerCase()}`; }

export function ExecutiveCommandView() {
  const [data, setData] = useState<ExecSummary | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const summary = await api.getExecutiveCommandSummary() as ExecSummary;
        setData(summary);
      } catch { /* ignore */ }
      finally { setLoading(false); }
    })();
  }, []);

  if (loading) return <div className="exec-view"><div className="exec-loading">Loading command summary...</div></div>;
  if (!data) return <div className="exec-view"><div className="exec-loading">Unable to load command summary.</div></div>;

  const { exceptionBrief: exc, approvalBrief: appr, calibrationBrief: cal,
          operationalBrief: ops, trustBrief: trust, scenarioBrief: scen, economicBrief: econ } = data;

  return (
    <div className="exec-view">
      <header className="exec-header">
        <h1 className="exec-title">Executive Command</h1>
        <p className="exec-subtitle">What changed. What matters. What needs your attention.</p>
        <p className="exec-ts">Generated {fmtDate(data.generatedAtUtc)}</p>
      </header>

      {/* ── Top-level signal cards ─────────────────────────── */}
      <div className="exec-signals">
        <div className={`exec-signal ${exc.critical > 0 ? 'exec-signal--critical' : 'exec-signal--good'}`}>
          <div className="exec-signal-value">{exc.critical}</div>
          <div className="exec-signal-label">Critical Exceptions</div>
        </div>
        <div className={`exec-signal ${exc.totalOpen > 0 ? 'exec-signal--warn' : 'exec-signal--good'}`}>
          <div className="exec-signal-value">{exc.totalOpen}</div>
          <div className="exec-signal-label">Open Exceptions</div>
        </div>
        <div className={`exec-signal ${appr.pendingCount > 0 ? 'exec-signal--warn' : 'exec-signal--neutral'}`}>
          <div className="exec-signal-value">{appr.pendingCount}</div>
          <div className="exec-signal-label">Pending Approvals</div>
        </div>
        <div className="exec-signal exec-signal--money">
          <div className="exec-signal-value">{fmtCurrency(econ.exceptionExposure)}</div>
          <div className="exec-signal-label">Economic Exposure</div>
        </div>
        <div className={`exec-signal ${econ.outcomesDrifting > 0 ? 'exec-signal--warn' : 'exec-signal--good'}`}>
          <div className="exec-signal-value">{econ.outcomesDrifting}</div>
          <div className="exec-signal-label">Outcomes Drifting</div>
        </div>
        <div className={`exec-signal ${ops.activeBottlenecks > 0 ? 'exec-signal--warn' : 'exec-signal--good'}`}>
          <div className="exec-signal-value">{ops.activeBottlenecks}</div>
          <div className="exec-signal-label">Bottlenecks</div>
        </div>
        <div className={`exec-signal ${cal.hitRate >= 0.7 ? 'exec-signal--good' : 'exec-signal--warn'}`}>
          <div className="exec-signal-value">{(cal.hitRate * 100).toFixed(0)}%</div>
          <div className="exec-signal-label">Decision Hit Rate</div>
        </div>
      </div>

      {/* ── Top exceptions ─────────────────────────────────── */}
      {exc.topExceptions.length > 0 && (
        <div className="exec-section">
          <Link to="/exceptions" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>
            What Needs Attention
            <span className="exec-section-count">{exc.totalOpen}</span>
          </Link>
          <div className="exec-exc-list">
            {exc.topExceptions.map(e => (
              <Link to="/exceptions" key={e.id} className="exec-exc-row" style={{ textDecoration: 'none' }}>
                <span className={`exec-badge ${sevClass(e.severity)}`}>{e.severity}</span>
                <span className="exec-exc-title">{e.title}</span>
                {e.recommendedActionType && <span className="exec-exc-action">{e.recommendedActionType}</span>}
                {e.economicImpactEstimate > 0 && <span className="exec-exc-impact">{fmtCurrency(e.economicImpactEstimate)}</span>}
                <span className="exec-exc-score">{e.priorityScore.toFixed(1)}</span>
              </Link>
            ))}
          </div>
        </div>
      )}

      {/* ── Pending approvals ──────────────────────────────── */}
      {appr.pendingCount > 0 && (
        <div className="exec-section">
          <Link to="/control" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>
            Awaiting Your Approval
            <span className="exec-section-count">{appr.pendingCount}</span>
          </Link>
          <div className="exec-approval-list">
            {appr.pendingApprovals.map(a => (
              <div key={a.id} className="exec-approval-row">
                <span className="exec-approval-type">{a.actionType}</span>
                <span className="exec-approval-just">{a.justification}</span>
                <span className="exec-approval-by">{a.requestedBy}</span>
                <span className="exec-approval-ts">{fmtDate(a.requestedAtUtc)}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Outcome calibration ────────────────────────────── */}
      {cal.totalOutcomes > 0 && (
        <div className="exec-section">
          <Link to="/decisions" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>Decision Calibration</Link>
          <div className="exec-cal-grid">
            <div className="exec-cal-item">
              <div className="exec-cal-val">{cal.totalOutcomes}</div>
              <div className="exec-cal-lbl">Outcomes</div>
            </div>
            <div className={`exec-cal-item ${cal.hitRate >= 0.7 ? 'exec-cal-good' : 'exec-cal-warn'}`}>
              <div className="exec-cal-val">{(cal.hitRate * 100).toFixed(0)}%</div>
              <div className="exec-cal-lbl">Hit Rate</div>
            </div>
            <div className={`exec-cal-item ${cal.underperformed > 0 ? 'exec-cal-bad' : ''}`}>
              <div className="exec-cal-val">{cal.underperformed}</div>
              <div className="exec-cal-lbl">Underperformed</div>
            </div>
            <div className={`exec-cal-item ${Math.abs(cal.meanVariancePercent) > 20 ? 'exec-cal-warn' : ''}`}>
              <div className="exec-cal-val">{cal.meanVariancePercent.toFixed(1)}%</div>
              <div className="exec-cal-lbl">Mean Variance</div>
            </div>
          </div>
        </div>
      )}

      {/* ── Operational twin ───────────────────────────────── */}
      <div className="exec-section">
        <Link to="/operational-twin" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>Operational Twin</Link>
        <div className="exec-ops-grid">
          {Object.entries(ops.entityCounts).map(([type, count]) => (
            <div key={type} className="exec-ops-item">
              <div className="exec-ops-val">{count}</div>
              <div className="exec-ops-lbl">{type}s</div>
            </div>
          ))}
          <div className="exec-ops-item">
            <div className="exec-ops-val">{ops.totalDependencies}</div>
            <div className="exec-ops-lbl">Deps</div>
          </div>
          <div className="exec-ops-item">
            <div className="exec-ops-val" style={{ color: ops.warningKpis > 0 ? '#FFBD2E' : undefined }}>{ops.warningKpis}</div>
            <div className="exec-ops-lbl">KPI Warnings</div>
          </div>
        </div>
        {ops.topBottlenecks.length > 0 && (
          <div className="exec-bn-list">
            {ops.topBottlenecks.map(b => (
              <div key={b.id} className="exec-bn-row">
                <span className={`exec-badge ${sevClass(b.severity)}`}>{b.severity}</span>
                <span className="exec-bn-desc">{b.description}</span>
                <span className="exec-bn-ts">{fmtDate(b.detectedAtUtc)}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* ── Trust tiers ────────────────────────────────────── */}
      {trust.totalPolicies > 0 && (
        <div className="exec-section">
          <Link to="/trust-tiers" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>
            AI Autonomy Controls
            <span className="exec-section-count">{trust.totalPolicies} policies</span>
          </Link>
          <div className="exec-tier-list">
            {Object.entries(trust.tierMap).map(([scope, tier]) => (
              <span key={scope} className="exec-tier-chip">{scope}<strong>{tier}</strong></span>
            ))}
          </div>
        </div>
      )}

      {/* ── Post-elite quick links ──────────────────────────── */}
      <div className="exec-section">
        <div className="exec-section-header">Governed Operations</div>
        <div className="exec-quick-links">
          <Link to="/hero-workflows" className="exec-quick-link">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><polygon points="5 3 19 12 5 21 5 3"/></svg>
            Hero Workflows
          </Link>
          <Link to="/simulation" className="exec-quick-link">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
            Dry-Run Simulation
          </Link>
          <Link to="/proof-analytics" className="exec-quick-link">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>
            Proof Analytics
          </Link>
          <Link to="/action-safety" className="exec-quick-link">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9.5 9l5 5m0-5l-5 5"/></svg>
            Action Safety
          </Link>
          <Link to="/inspection" className="exec-quick-link">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
            Operator Inspection
          </Link>
        </div>
      </div>

      {/* ── Scenarios ──────────────────────────────────────── */}
      {scen.recentScenarios.length > 0 && (
        <div className="exec-section">
          <Link to="/scenarios" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>
            Scenario Planning
            <span className="exec-section-count">{scen.totalActive} active / {scen.totalCompared} compared</span>
          </Link>
          <div className="exec-scenario-list">
            {scen.recentScenarios.map(s => (
              <Link to="/scenarios" key={s.id} className="exec-scenario-row" style={{ textDecoration: 'none' }}>
                <span className="exec-badge exec-sev-info">{s.type}</span>
                <span className="exec-scenario-title">{s.title}</span>
                <span className="exec-scenario-meta">{s.assumptionCount} assumptions</span>
                <span className="exec-scenario-meta">{s.effectCount} effects</span>
                <span className="exec-scenario-meta">{fmtDate(s.updatedAtUtc)}</span>
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
