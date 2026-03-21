import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import './executive-command.css';

interface IndustryKpi {
  id: string;
  label: string;
  value: number;
  unit: string;
  trend?: string;
  trendDelta?: number;
  priorPeriodValue?: number;
  severity?: string;
  economicExposure?: number;
  description: string;
}

interface IndustryKpiResponse {
  industry: string;
  generatedAtUtc: string;
  kpis: IndustryKpi[];
}

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
interface ProofBrief {
  totalDecisions: number;
  withOutcomes: number;
  accuracyRate: number;
  successRate: number;
  overrideRate: number;
}
interface ActionSafetyBrief {
  totalActions: number;
  reversible: number;
  irreversible: number;
  rollbacksSucceeded: number;
  rollbacksFailed: number;
}
interface WorkflowHeadline {
  id: string;
  workflowType: string;
  title: string;
  status: string;
  completedSteps: number;
  totalSteps: number;
  updatedAtUtc: string;
}
interface WorkflowBrief {
  active: number;
  completed: number;
  failed: number;
  recent: WorkflowHeadline[];
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
  proofBrief: ProofBrief;
  actionSafetyBrief: ActionSafetyBrief;
  workflowBrief: WorkflowBrief;
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
  const [industryKpis, setIndustryKpis] = useState<IndustryKpiResponse | null>(null);
  const [industryKpisLoading, setIndustryKpisLoading] = useState(false);
  const [healthcareKpis, setHealthcareKpis] = useState<IndustryKpiResponse | null>(null);
  const [healthcareKpisLoading, setHealthcareKpisLoading] = useState(false);
  const [financialKpis, setFinancialKpis] = useState<IndustryKpiResponse | null>(null);
  const [financialKpisLoading, setFinancialKpisLoading] = useState(false);
  const [energyKpis, setEnergyKpis] = useState<IndustryKpiResponse | null>(null);
  const [energyKpisLoading, setEnergyKpisLoading] = useState(false);
  const [defenseKpis, setDefenseKpis] = useState<IndustryKpiResponse | null>(null);
  const [defenseKpisLoading, setDefenseKpisLoading] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const summary = await api.getExecutiveCommandSummary() as ExecSummary;
        setData(summary);
      } catch { /* ignore */ }
      finally { setLoading(false); }
    })();
  }, []);

  useEffect(() => {
    // Attempt to load manufacturing KPIs — hides section if null
    setIndustryKpisLoading(true);
    (async () => {
      try {
        const kpis = await api.getIndustryKpis('manufacturing') as IndustryKpiResponse | null;
        setIndustryKpis(kpis);
      } catch { /* ignore */ }
      finally { setIndustryKpisLoading(false); }
    })();
  }, []);

  useEffect(() => {
    // Attempt to load healthcare KPIs — hides section if null
    setHealthcareKpisLoading(true);
    (async () => {
      try {
        const kpis = await api.getIndustryKpis('healthcare') as IndustryKpiResponse | null;
        setHealthcareKpis(kpis);
      } catch { /* ignore */ }
      finally { setHealthcareKpisLoading(false); }
    })();
  }, []);

  useEffect(() => {
    // Attempt to load financial services KPIs — hides section if null
    setFinancialKpisLoading(true);
    (async () => {
      try {
        const kpis = await api.getIndustryKpis('financial-services') as IndustryKpiResponse | null;
        setFinancialKpis(kpis);
      } catch { /* ignore */ }
      finally { setFinancialKpisLoading(false); }
    })();
  }, []);

  useEffect(() => {
    // Attempt to load energy KPIs — hides section if null
    setEnergyKpisLoading(true);
    (async () => {
      try {
        const kpis = await api.getIndustryKpis('energy') as IndustryKpiResponse | null;
        setEnergyKpis(kpis);
      } catch { /* ignore */ }
      finally { setEnergyKpisLoading(false); }
    })();
  }, []);

  useEffect(() => {
    // Attempt to load defense KPIs — hides section if null
    setDefenseKpisLoading(true);
    (async () => {
      try {
        const kpis = await api.getIndustryKpis('defense') as IndustryKpiResponse | null;
        setDefenseKpis(kpis);
      } catch { /* ignore */ }
      finally { setDefenseKpisLoading(false); }
    })();
  }, []);

  if (loading) return <div className="exec-view"><div className="exec-loading">Loading command summary...</div></div>;
  if (!data) return <div className="exec-view"><div className="exec-loading">Unable to load command summary.</div></div>;

  const { exceptionBrief: exc, approvalBrief: appr, calibrationBrief: cal,
          operationalBrief: ops, trustBrief: trust, scenarioBrief: scen, economicBrief: econ,
          proofBrief: proof, actionSafetyBrief: safety, workflowBrief: wf } = data;

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

      {/* ── Production Intelligence (manufacturing) ────────── */}
      {industryKpisLoading && (
        <div className="exec-section">
          <div className="exec-section-header">Production Intelligence</div>
          <div className="exec-cal-grid">
            {[1,2,3,4,5,6].map(i => (
              <div key={i} className="exec-cal-item exec-shimmer" />
            ))}
          </div>
        </div>
      )}
      {!industryKpisLoading && industryKpis && industryKpis.kpis && (
        <div className="exec-section">
          <div className="exec-section-header">Production Intelligence</div>
          <div className="exec-cal-grid">
            {industryKpis.kpis.map(kpi => (
              <div key={kpi.id} className={`exec-cal-item ${
                kpi.trend === 'up' && kpi.unit === 'percent' ? 'exec-cal-good' :
                kpi.trend === 'down' && kpi.unit === 'hours' ? 'exec-cal-good' :
                kpi.severity === 'high' || kpi.severity === 'critical' ? 'exec-cal-bad' :
                ''
              }`}>
                <div className="exec-cal-val">
                  {kpi.unit === 'percent' ? `${kpi.value}%` :
                   kpi.unit === 'hours' ? `${kpi.value}h` :
                   kpi.economicExposure ? `${kpi.value}` :
                   kpi.value}
                  {kpi.trend && (
                    <span className="exec-kpi-trend" style={{ fontSize: '12px', marginLeft: '4px' }}>
                      {kpi.trend === 'up' ? '\u2191' : kpi.trend === 'down' ? '\u2193' : '\u2192'}
                    </span>
                  )}
                </div>
                <div className="exec-cal-lbl">{kpi.label}</div>
                {kpi.trendDelta != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: kpi.trendDelta > 0 ? '#34D399' : '#F87171', marginTop: '2px' }}>
                    {kpi.trendDelta > 0 ? '+' : ''}{kpi.trendDelta}{kpi.unit === 'percent' ? 'pp' : kpi.unit === 'hours' ? 'h' : ''}
                  </div>
                )}
                {kpi.economicExposure != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: '#A78BFA', marginTop: '2px' }}>
                    {fmtCurrency(kpi.economicExposure)} exposure
                  </div>
                )}
                {kpi.priorPeriodValue != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: 'var(--text-3)', marginTop: '2px' }}>
                    Prior: {kpi.priorPeriodValue}{kpi.unit === 'hours' ? 'h' : ''}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Clinical Operations Intelligence (healthcare) ──── */}
      {healthcareKpisLoading && (
        <div className="exec-section">
          <div className="exec-section-header">Clinical Operations Intelligence</div>
          <div className="exec-cal-grid">
            {[1,2,3,4,5,6].map(i => (
              <div key={i} className="exec-cal-item exec-shimmer" />
            ))}
          </div>
        </div>
      )}
      {!healthcareKpisLoading && healthcareKpis && healthcareKpis.kpis && (
        <div className="exec-section">
          <div className="exec-section-header">Clinical Operations Intelligence</div>
          <div className="exec-cal-grid">
            {healthcareKpis.kpis.map(kpi => (
              <div key={kpi.id} className={`exec-cal-item ${
                kpi.trend === 'up' && kpi.unit === 'percent' ? 'exec-cal-good' :
                kpi.trend === 'down' && (kpi.unit === 'hours' || kpi.unit === 'days' || kpi.unit === 'percent') ? 'exec-cal-good' :
                kpi.trend === 'up' && kpi.unit === 'patients/bed/day' ? 'exec-cal-good' :
                ''
              }`}>
                <div className="exec-cal-val">
                  {kpi.unit === 'percent' ? `${kpi.value}%` :
                   kpi.unit === 'hours' ? `${kpi.value}h` :
                   kpi.unit === 'days' ? `${kpi.value}d` :
                   kpi.unit === 'patients/bed/day' ? `${kpi.value}` :
                   kpi.value}
                  {kpi.trend && (
                    <span className="exec-kpi-trend" style={{ fontSize: '12px', marginLeft: '4px' }}>
                      {kpi.trend === 'up' ? '\u2191' : kpi.trend === 'down' ? '\u2193' : '\u2192'}
                    </span>
                  )}
                </div>
                <div className="exec-cal-lbl">{kpi.label}</div>
                {kpi.trendDelta != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: kpi.trendDelta < 0 ? '#34D399' : '#34D399', marginTop: '2px' }}>
                    {kpi.trendDelta > 0 ? '+' : ''}{kpi.trendDelta}{kpi.unit === 'percent' ? 'pp' : kpi.unit === 'hours' ? 'h' : kpi.unit === 'days' ? 'd' : ''}
                  </div>
                )}
                {kpi.priorPeriodValue != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: 'var(--text-3)', marginTop: '2px' }}>
                    Prior: {kpi.priorPeriodValue}{kpi.unit === 'hours' ? 'h' : ''}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Financial Operations Intelligence ──────────────── */}
      {financialKpisLoading && (
        <div className="exec-section">
          <div className="exec-section-header">Financial Operations Intelligence</div>
          <div className="exec-cal-grid">
            {[1,2,3,4,5,6].map(i => (
              <div key={i} className="exec-cal-item exec-shimmer" />
            ))}
          </div>
        </div>
      )}
      {!financialKpisLoading && financialKpis && financialKpis.kpis && (
        <div className="exec-section">
          <div className="exec-section-header">Financial Operations Intelligence</div>
          <div className="exec-cal-grid">
            {financialKpis.kpis.map(kpi => (
              <div key={kpi.id} className={`exec-cal-item ${
                kpi.trend === 'up' && kpi.unit === 'percent' ? 'exec-cal-good' :
                kpi.trend === 'down' && (kpi.unit === 'days' || kpi.unit === 'count') ? 'exec-cal-good' :
                kpi.severity === 'high' || kpi.severity === 'critical' ? 'exec-cal-bad' :
                ''
              }`}>
                <div className="exec-cal-val">
                  {kpi.unit === 'percent' ? `${kpi.value}%` :
                   kpi.unit === 'days' ? `${kpi.value}d` :
                   kpi.unit === 'dollars' ? fmtCurrency(kpi.value) :
                   kpi.value}
                  {kpi.trend && (
                    <span className="exec-kpi-trend" style={{ fontSize: '12px', marginLeft: '4px' }}>
                      {kpi.trend === 'up' ? '\u2191' : kpi.trend === 'down' ? '\u2193' : '\u2192'}
                    </span>
                  )}
                </div>
                <div className="exec-cal-lbl">{kpi.label}</div>
                {kpi.trendDelta != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: kpi.trendDelta > 0 && kpi.unit === 'percent' ? '#34D399' : kpi.trendDelta < 0 ? '#34D399' : '#F87171', marginTop: '2px' }}>
                    {kpi.trendDelta > 0 ? '+' : ''}{kpi.trendDelta}{kpi.unit === 'percent' ? 'pp' : kpi.unit === 'days' ? 'd' : ''}
                  </div>
                )}
                {kpi.economicExposure != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: '#A78BFA', marginTop: '2px' }}>
                    {fmtCurrency(kpi.economicExposure)} exposure
                  </div>
                )}
                {kpi.priorPeriodValue != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: 'var(--text-3)', marginTop: '2px' }}>
                    Prior: {kpi.priorPeriodValue}{kpi.unit === 'days' ? 'd' : ''}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Grid & Asset Intelligence (energy) ───────────── */}
      {energyKpisLoading && (
        <div className="exec-section">
          <div className="exec-section-header">Grid &amp; Asset Intelligence</div>
          <div className="exec-cal-grid">
            {[1,2,3,4,5,6].map(i => (
              <div key={i} className="exec-cal-item exec-shimmer" />
            ))}
          </div>
        </div>
      )}
      {!energyKpisLoading && energyKpis && energyKpis.kpis && (
        <div className="exec-section">
          <div className="exec-section-header">Grid &amp; Asset Intelligence</div>
          <div className="exec-cal-grid">
            {energyKpis.kpis.map(kpi => (
              <div key={kpi.id} className={`exec-cal-item ${
                kpi.trend === 'up' && kpi.unit === 'percent' ? 'exec-cal-good' :
                kpi.trend === 'down' && (kpi.unit === 'minutes' || kpi.unit === 'rate') ? 'exec-cal-good' :
                kpi.severity === 'high' || kpi.severity === 'critical' ? 'exec-cal-bad' :
                ''
              }`}>
                <div className="exec-cal-val">
                  {kpi.unit === 'percent' ? `${kpi.value}%` :
                   kpi.unit === 'hours' ? `${kpi.value}h` :
                   kpi.unit === 'minutes' ? `${kpi.value}m` :
                   kpi.value}
                  {kpi.trend && (
                    <span className="exec-kpi-trend" style={{ fontSize: '12px', marginLeft: '4px' }}>
                      {kpi.trend === 'up' ? '\u2191' : kpi.trend === 'down' ? '\u2193' : '\u2192'}
                    </span>
                  )}
                </div>
                <div className="exec-cal-lbl">{kpi.label}</div>
                {kpi.trendDelta != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: kpi.trendDelta > 0 && kpi.unit === 'percent' ? '#34D399' : kpi.trendDelta < 0 ? '#34D399' : '#F87171', marginTop: '2px' }}>
                    {kpi.trendDelta > 0 ? '+' : ''}{kpi.trendDelta}{kpi.unit === 'percent' ? 'pp' : kpi.unit === 'hours' ? 'h' : kpi.unit === 'minutes' ? 'm' : ''}
                  </div>
                )}
                {kpi.priorPeriodValue != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: 'var(--text-3)', marginTop: '2px' }}>
                    Prior: {kpi.priorPeriodValue}{kpi.unit === 'hours' ? 'h' : kpi.unit === 'minutes' ? 'm' : ''}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Mission Readiness Intelligence (defense) ─────── */}
      {defenseKpisLoading && (
        <div className="exec-section">
          <div className="exec-section-header">Mission Readiness Intelligence</div>
          <div className="exec-cal-grid">
            {[1,2,3,4,5,6].map(i => (
              <div key={i} className="exec-cal-item exec-shimmer" />
            ))}
          </div>
        </div>
      )}
      {!defenseKpisLoading && defenseKpis && defenseKpis.kpis && (
        <div className="exec-section">
          <div className="exec-section-header">Mission Readiness Intelligence</div>
          <div className="exec-cal-grid">
            {defenseKpis.kpis.map(kpi => (
              <div key={kpi.id} className={`exec-cal-item ${
                kpi.trend === 'up' && kpi.unit === 'percent' ? 'exec-cal-good' :
                kpi.trend === 'down' && kpi.unit === 'count' ? 'exec-cal-good' :
                kpi.severity === 'high' || kpi.severity === 'critical' ? 'exec-cal-bad' :
                ''
              }`}>
                <div className="exec-cal-val">
                  {kpi.unit === 'percent' ? `${kpi.value}%` :
                   kpi.unit === 'score' ? `${kpi.value}` :
                   kpi.value}
                  {kpi.trend && (
                    <span className="exec-kpi-trend" style={{ fontSize: '12px', marginLeft: '4px' }}>
                      {kpi.trend === 'up' ? '\u2191' : kpi.trend === 'down' ? '\u2193' : '\u2192'}
                    </span>
                  )}
                </div>
                <div className="exec-cal-lbl">{kpi.label}</div>
                {kpi.trendDelta != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: kpi.trendDelta > 0 && kpi.unit === 'percent' ? '#34D399' : kpi.trendDelta < 0 && kpi.unit === 'count' ? '#34D399' : kpi.trendDelta > 0 ? '#34D399' : '#F87171', marginTop: '2px' }}>
                    {kpi.trendDelta > 0 ? '+' : ''}{kpi.trendDelta}{kpi.unit === 'percent' ? 'pp' : ''}
                  </div>
                )}
                {kpi.priorPeriodValue != null && (
                  <div style={{ fontFamily: 'var(--mono)', fontSize: '10px', color: 'var(--text-3)', marginTop: '2px' }}>
                    Prior: {kpi.priorPeriodValue}{kpi.unit === 'percent' ? '%' : ''}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

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

      {/* ── Proof, Safety & Workflow metrics ─────────────────── */}
      <div className="exec-section">
        <Link to="/proof-analytics" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>Proof Analytics</Link>
        <div className="exec-cal-grid">
          <div className="exec-cal-item">
            <div className="exec-cal-val">{proof.totalDecisions}</div>
            <div className="exec-cal-lbl">Decisions</div>
          </div>
          <div className="exec-cal-item">
            <div className="exec-cal-val">{proof.withOutcomes}</div>
            <div className="exec-cal-lbl">With Outcomes</div>
          </div>
          <div className={`exec-cal-item ${proof.accuracyRate >= 0.7 ? 'exec-cal-good' : 'exec-cal-warn'}`}>
            <div className="exec-cal-val">{(proof.accuracyRate * 100).toFixed(0)}%</div>
            <div className="exec-cal-lbl">Accuracy</div>
          </div>
          <div className={`exec-cal-item ${proof.successRate >= 0.8 ? 'exec-cal-good' : 'exec-cal-warn'}`}>
            <div className="exec-cal-val">{(proof.successRate * 100).toFixed(0)}%</div>
            <div className="exec-cal-lbl">Exec Success</div>
          </div>
          <div className={`exec-cal-item ${proof.overrideRate <= 0.1 ? '' : 'exec-cal-bad'}`}>
            <div className="exec-cal-val">{(proof.overrideRate * 100).toFixed(0)}%</div>
            <div className="exec-cal-lbl">Override Rate</div>
          </div>
        </div>
      </div>

      <div className="exec-section">
        <Link to="/action-safety" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>Action Safety</Link>
        <div className="exec-cal-grid">
          <div className="exec-cal-item">
            <div className="exec-cal-val">{safety.totalActions}</div>
            <div className="exec-cal-lbl">Actions</div>
          </div>
          <div className="exec-cal-item exec-cal-good">
            <div className="exec-cal-val">{safety.reversible}</div>
            <div className="exec-cal-lbl">Reversible</div>
          </div>
          <div className={`exec-cal-item ${safety.irreversible > 0 ? 'exec-cal-bad' : ''}`}>
            <div className="exec-cal-val">{safety.irreversible}</div>
            <div className="exec-cal-lbl">Irreversible</div>
          </div>
          <div className="exec-cal-item exec-cal-good">
            <div className="exec-cal-val">{safety.rollbacksSucceeded}</div>
            <div className="exec-cal-lbl">Rollbacks OK</div>
          </div>
          <div className={`exec-cal-item ${safety.rollbacksFailed > 0 ? 'exec-cal-bad' : ''}`}>
            <div className="exec-cal-val">{safety.rollbacksFailed}</div>
            <div className="exec-cal-lbl">Rollbacks Failed</div>
          </div>
        </div>
      </div>

      <div className="exec-section">
        <Link to="/hero-workflows" className="exec-section-header" style={{ textDecoration: 'none', color: 'inherit' }}>
          Hero Workflows
          <span className="exec-section-count">{wf.active} active / {wf.completed} done / {wf.failed} failed</span>
        </Link>
        {wf.recent.length > 0 && (
          <div className="exec-scenario-list">
            {wf.recent.map(w => (
              <Link to="/hero-workflows" key={w.id} className="exec-scenario-row" style={{ textDecoration: 'none' }}>
                <span className={`exec-badge ${w.status === 'Failed' ? 'exec-sev-critical' : w.status === 'Completed' ? 'exec-sev-info' : 'exec-sev-high'}`}>{w.status}</span>
                <span className="exec-scenario-title">{w.title}</span>
                <span className="exec-scenario-meta">{w.completedSteps}/{w.totalSteps} steps</span>
                <span className="exec-scenario-meta">{fmtDate(w.updatedAtUtc)}</span>
              </Link>
            ))}
          </div>
        )}
      </div>

      {/* ── Quick links ──────────────────────────────────────── */}
      <div className="exec-section">
        <div className="exec-section-header">More Views</div>
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
          <Link to="/trust-lineage" className="exec-quick-link">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M10 13a5 5 0 007.54.54l3-3a5 5 0 00-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 00-7.54-.54l-3 3a5 5 0 007.07 7.07l1.71-1.71"/></svg>
            Trust Lineage
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
