import { useState, useRef, useCallback, useEffect } from 'react';
import { useDemoRunner } from './hooks/useDemoRunner';
import type {
  DemoAct,
  DemoScenario,
  TrustTierInfo,
  ComparisonRow,
  AiReasoningOutput,
  PolicyEvaluationOutput,
  ApprovalGateOutput,
  GatedExecutionOutput,
  OutcomeRecordingOutput,
  DemoPhase,
} from './types';
import './demo-showcase.css';

/* ── Static data ────────────────────────────────────────────────── */

const TRUST_TIERS: TrustTierInfo[] = [
  {
    tier: 'T0',
    label: 'Observe Only',
    description: 'Agent can see, cannot act',
    example:
      'A new agent analyzing market data starts at T0. It can read dashboards and generate reports, but cannot place trades or modify portfolios.',
  },
  {
    tier: 'T1',
    label: 'Recommend',
    description: 'Agent suggests, human decides',
    example:
      'After 30 days of accurate predictions, the agent graduates to T1. It now suggests trade opportunities, but a human analyst must approve each one.',
  },
  {
    tier: 'T2',
    label: 'Act with Approval',
    description: 'Agent drafts, human approves',
    example:
      'The agent drafts complete trade orders with risk analysis. A portfolio manager reviews and clicks approve — the agent executes.',
  },
  {
    tier: 'T3',
    label: 'Act & Notify',
    description: 'Agent executes, human is informed',
    example:
      'For trades under $10K in approved asset classes, the agent executes immediately and sends a notification to the team channel.',
  },
  {
    tier: 'T4',
    label: 'Full Autonomy',
    description: 'Agent executes within policy bounds',
    example:
      'The agent manages a defined portfolio allocation strategy end-to-end, rebalancing within pre-approved risk parameters without human intervention.',
  },
  {
    tier: 'T5',
    label: 'Emergency Override',
    description: 'Agent acts with post-hoc audit',
    example:
      'During a flash crash, the agent triggers emergency hedging protocols. All actions are cryptographically signed and submitted for mandatory post-hoc review.',
  },
];

const COMPARISON_DATA: ComparisonRow[] = [
  {
    capability: 'Trust-tiered autonomy',
    archonai: '6 levels',
    workato: '\u2715',
    boomi: '\u2715',
    trayai: '\u2715',
  },
  {
    capability: 'Cryptographic override signing',
    archonai: 'HMAC-SHA256',
    workato: '\u2715',
    boomi: '\u2715',
    trayai: '\u2715',
  },
  {
    capability: 'Separation of duties enforcement',
    archonai: '\u2713',
    workato: 'Basic',
    boomi: '\u2715',
    trayai: '\u2715',
  },
  {
    capability: 'Policy simulation / dry-run',
    archonai: 'Full pipeline',
    workato: '\u2715',
    boomi: '\u2715',
    trayai: '\u2715',
  },
  {
    capability: 'Decision-to-outcome proof analytics',
    archonai: '12 event types',
    workato: '\u2715',
    boomi: '\u2715',
    trayai: '\u2715',
  },
  {
    capability: 'Action reversibility classification',
    archonai: '5 strategies',
    workato: '\u2715',
    boomi: '\u2715',
    trayai: '\u2715',
  },
  {
    capability: 'Hash-chained immutable audit',
    archonai: 'SHA-256',
    workato: 'Logs only',
    boomi: 'Logs only',
    trayai: 'Logs only',
  },
  {
    capability: 'External Governance API',
    archonai: 'REST',
    workato: '\u2715',
    boomi: '\u2715',
    trayai: '\u2715',
  },
];

const PHASE_META: Record<string, { icon: string; label: string }> = {
  'ai-reasoning': { icon: '\u2728', label: 'AI Reasoning' },
  'policy-evaluation': { icon: '\u2696', label: 'Policy Evaluation' },
  'approval-gate': { icon: '\u{1F512}', label: 'Approval Gate' },
  'gated-execution': { icon: '\u26A1', label: 'Gated Execution' },
  'outcome-recording': { icon: '\u{1F4CB}', label: 'Outcome Recording' },
};

const SCENARIO_LABELS: Record<DemoScenario, string> = {
  'finance-approval': 'Financial Approval ($500K)',
  'sales-anomaly': 'Sales Anomaly (EMEA +35%)',
  'ops-escalation': 'Ops Escalation (3 Failures)',
};

/* ── Phase detail renderers ─────────────────────────────────────── */

function parsePhaseOutput(phase: DemoPhase): unknown {
  try {
    return JSON.parse(phase.output);
  } catch {
    return null;
  }
}

function RiskBar({ score }: { score: number }) {
  const level = score >= 70 ? 'high' : score >= 40 ? 'medium' : 'low';
  return (
    <div className="demo-risk-bar">
      <div
        className={`demo-risk-fill ${level}`}
        style={{ width: `${score}%` }}
      />
    </div>
  );
}

function PhaseDetail({
  phase,
  phaseIndex,
}: {
  phase: DemoPhase;
  phaseIndex: number;
}) {
  const data = parsePhaseOutput(phase);
  if (!data) return null;
  const meta = PHASE_META[phase.name] ?? { icon: '\u25CF', label: phase.name };

  return (
    <div className="demo-phase-detail">
      <div className="demo-phase-detail-header">
        <span className="demo-phase-detail-icon">{meta.icon}</span>
        <span className="demo-phase-detail-title">
          Phase {phaseIndex + 1}: {meta.label}
        </span>
        <span className="demo-phase-detail-latency">
          {phase.latencyMs.toFixed(0)}ms
        </span>
      </div>
      {phase.name === 'ai-reasoning' && (
        <AiReasoningDetail data={data as AiReasoningOutput} />
      )}
      {phase.name === 'policy-evaluation' && (
        <PolicyEvaluationDetail data={data as PolicyEvaluationOutput} />
      )}
      {phase.name === 'approval-gate' && (
        <ApprovalGateDetail data={data as ApprovalGateOutput} />
      )}
      {phase.name === 'gated-execution' && (
        <GatedExecutionDetail data={data as GatedExecutionOutput} />
      )}
      {phase.name === 'outcome-recording' && (
        <OutcomeRecordingDetail data={data as OutcomeRecordingOutput} />
      )}
    </div>
  );
}

function AiReasoningDetail({ data }: { data: AiReasoningOutput }) {
  return (
    <div className="demo-phase-fields">
      <div className="demo-field demo-field-prose">{data.response}</div>
      <div className="demo-field">
        <span className="demo-field-label">Model</span>
        <span className="demo-field-value">{data.model}</span>
      </div>
      <div className="demo-field">
        <span className="demo-field-label">Tokens Used</span>
        <span className="demo-field-value">{data.tokensUsed}</span>
      </div>
    </div>
  );
}

function PolicyEvaluationDetail({ data }: { data: PolicyEvaluationOutput }) {
  return (
    <div className="demo-phase-fields">
      <div className="demo-field">
        <span className="demo-field-label">Risk Score</span>
        <span className="demo-field-value">{data.riskScore}/100</span>
        <RiskBar score={data.riskScore} />
      </div>
      <div className="demo-field">
        <span className="demo-field-label">Confidence</span>
        <span className="demo-field-value">
          {(data.confidenceScore * 100).toFixed(0)}%
        </span>
      </div>
      <div className="demo-field">
        <span className="demo-field-label">Allowed</span>
        <span className={`demo-field-value ${data.isAllowed ? 'success' : 'error'}`}>
          {data.isAllowed ? 'Yes' : 'No'}
        </span>
      </div>
      <div className="demo-field">
        <span className="demo-field-label">Requires Approval</span>
        <span className={`demo-field-value ${data.requiresApproval ? 'warning' : 'success'}`}>
          {data.requiresApproval ? 'Yes' : 'No'}
        </span>
      </div>
      <div className="demo-field" style={{ gridColumn: '1 / -1' }}>
        <span className="demo-field-label">Reason</span>
        <span className="demo-field-value">{data.reason}</span>
      </div>
    </div>
  );
}

function ApprovalGateDetail({ data }: { data: ApprovalGateOutput }) {
  return (
    <div className="demo-phase-fields">
      <div className="demo-field">
        <span className="demo-field-label">Gate ID</span>
        <span className="demo-field-value">{data.approvalGateId}</span>
      </div>
      <div className="demo-field">
        <span className="demo-field-label">Status</span>
        <span className={`demo-field-value ${data.status === 'auto-approved' ? 'success' : 'warning'}`}>
          {data.status}
        </span>
      </div>
      <div className="demo-field">
        <span className="demo-field-label">Override Token</span>
        <span className={`demo-field-value ${data.hasOverrideToken ? 'success' : ''}`}>
          {data.hasOverrideToken ? 'HMAC-SHA256 Signed' : 'None'}
        </span>
      </div>
    </div>
  );
}

function GatedExecutionDetail({ data }: { data: GatedExecutionOutput }) {
  return (
    <div className="demo-phase-fields">
      <div className="demo-field">
        <span className="demo-field-label">Executed</span>
        <span className={`demo-field-value ${data.executed ? 'success' : 'warning'}`}>
          {data.executed ? 'Yes' : 'No'}
        </span>
      </div>
      <div className="demo-field">
        <span className="demo-field-label">Success</span>
        <span
          className={`demo-field-value ${data.success === true ? 'success' : data.success === false ? 'error' : ''}`}
        >
          {data.success === null ? 'N/A' : data.success ? 'Yes' : 'No'}
        </span>
      </div>
      {data.error && (
        <div className="demo-field" style={{ gridColumn: '1 / -1' }}>
          <span className="demo-field-label">Error</span>
          <span className="demo-field-value error">{data.error}</span>
        </div>
      )}
    </div>
  );
}

function OutcomeRecordingDetail({ data }: { data: OutcomeRecordingOutput }) {
  return (
    <div className="demo-phase-fields">
      <div className="demo-field">
        <span className="demo-field-label">Decision ID</span>
        <span className="demo-field-value">{data.decisionId}</span>
      </div>
      <div className="demo-field">
        <span className="demo-field-label">Confidence at Prediction</span>
        <span className="demo-field-value">
          {(data.confidenceAtPrediction * 100).toFixed(0)}%
        </span>
      </div>
      <div className="demo-field" style={{ gridColumn: '1 / -1' }}>
        <span className="demo-field-label">Expected Outcome</span>
        <span className="demo-field-value">{data.expectedOutcomeSummary}</span>
      </div>
    </div>
  );
}

/* ── Main Component ─────────────────────────────────────────────── */

export function GovernanceDemoShowcase() {
  const [currentAct, setCurrentAct] = useState<DemoAct>(1);
  const [selectedScenario, setSelectedScenario] =
    useState<DemoScenario>('finance-approval');
  const [expandedTier, setExpandedTier] = useState<number | null>(null);
  const [selectedPhaseIndex, setSelectedPhaseIndex] = useState<number | null>(null);

  const actRefs = useRef<(HTMLElement | null)[]>([]);
  const demo = useDemoRunner();

  const scrollToAct = useCallback((act: DemoAct) => {
    setCurrentAct(act);
    const el = actRefs.current[act - 1];
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }, []);

  const handleRunScenario = useCallback(() => {
    setSelectedPhaseIndex(null);
    demo.runScenario(selectedScenario);
  }, [demo, selectedScenario]);

  const handleScenarioChange = useCallback(
    (scenario: DemoScenario) => {
      setSelectedScenario(scenario);
      demo.reset();
      setSelectedPhaseIndex(null);
    },
    [demo],
  );

  // Update progress bar based on scroll position
  useEffect(() => {
    const handleScroll = () => {
      const scrollY = window.scrollY + window.innerHeight / 2;
      for (let i = actRefs.current.length - 1; i >= 0; i--) {
        const el = actRefs.current[i];
        if (el && el.offsetTop <= scrollY) {
          setCurrentAct((i + 1) as DemoAct);
          break;
        }
      }
    };
    window.addEventListener('scroll', handleScroll, { passive: true });
    return () => window.removeEventListener('scroll', handleScroll);
  }, []);

  // Auto-select the active phase in the detail panel during animation
  useEffect(() => {
    if (demo.activePhaseIndex >= 0) {
      setSelectedPhaseIndex(demo.activePhaseIndex);
    }
  }, [demo.activePhaseIndex]);

  const activePhase =
    selectedPhaseIndex !== null && demo.result
      ? demo.result.phases[selectedPhaseIndex]
      : null;

  return (
    <div className="demo-showcase">
      {/* Progress bar */}
      <div className="demo-progress-bar">
        {[1, 2, 3, 4, 5].map((act) => (
          <button
            key={act}
            className={`demo-progress-segment${currentAct >= act ? ' active' : ''}`}
            onClick={() => scrollToAct(act as DemoAct)}
            aria-label={`Go to act ${act}`}
          />
        ))}
      </div>

      {/* ── ACT 1: THE PLATFORM ─────────────────────────────────── */}
      <section
        className="demo-act demo-hero"
        ref={(el) => { actRefs.current[0] = el; }}
      >
        <div className="demo-logo">
          Archon<span className="demo-logo-accent">AI</span>
        </div>
        <div className="demo-tagline">
          Enterprise AI Governance. Decide. Approve. Prove.
        </div>

        <div className="demo-stats-row">
          <div className="demo-stat-card">
            <div className="demo-stat-value">107K</div>
            <div className="demo-stat-label">Lines of Production Code</div>
          </div>
          <div className="demo-stat-card">
            <div className="demo-stat-value">33</div>
            <div className="demo-stat-label">PostgreSQL-Backed Stores</div>
          </div>
          <div className="demo-stat-card">
            <div className="demo-stat-value">1,285</div>
            <div className="demo-stat-label">Automated Tests</div>
          </div>
        </div>

        <p className="demo-hero-description">
          The only platform shipping trust-tiered autonomy, cryptographic
          override signing, and decision-to-outcome proof analytics.
        </p>

        <button className="demo-begin-btn" onClick={() => scrollToAct(2)}>
          Begin Demo &#8594;
        </button>
      </section>

      {/* ── ACT 2: THE GOVERNANCE PIPELINE ──────────────────────── */}
      <section
        className="demo-act demo-pipeline-section"
        ref={(el) => { actRefs.current[1] = el; }}
      >
        <div className="demo-act-inner">
          <div className="demo-section-label">Act 2</div>
          <h2 className="demo-act-title">The Governance Pipeline</h2>
          <p className="demo-act-subtitle">
            Every AI action flows through a 5-phase governance pipeline.
            Select a scenario and watch it execute in real time.
          </p>

          {/* Scenario selector + run button */}
          <div className="demo-scenario-bar">
            {(
              Object.entries(SCENARIO_LABELS) as [DemoScenario, string][]
            ).map(([key, label]) => (
              <button
                key={key}
                className={`demo-scenario-btn${selectedScenario === key ? ' active' : ''}`}
                onClick={() => handleScenarioChange(key)}
              >
                {label}
              </button>
            ))}
            <button
              className="demo-run-btn"
              onClick={handleRunScenario}
              disabled={demo.isLoading || demo.isAnimating}
            >
              {demo.isLoading ? (
                <>
                  <span className="demo-spinner" /> Running…
                </>
              ) : (
                'Run Scenario'
              )}
            </button>
            {demo.isMockData && demo.result && (
              <span className="demo-mock-badge">Mock Data</span>
            )}
          </div>

          {/* Pipeline visualization */}
          <div className="demo-pipeline">
            {Object.entries(PHASE_META).map(([phaseName, meta], index) => {
              const isCompleted =
                demo.result !== null && index < demo.activePhaseIndex;
              const isActive =
                demo.result !== null && index === demo.activePhaseIndex;
              const isDim = demo.result === null || index > demo.activePhaseIndex;
              const phase = demo.result?.phases[index];

              return (
                <div key={phaseName} className="demo-pipeline-node">
                  <div
                    className={`demo-phase-box${isDim ? ' dim' : ''}${isActive ? ' active' : ''}${isCompleted ? ' completed' : ''}`}
                    onClick={() => {
                      if (!isDim && phase) setSelectedPhaseIndex(index);
                    }}
                  >
                    <div className="demo-phase-icon">{meta.icon}</div>
                    <div className="demo-phase-name">{meta.label}</div>
                    {phase && !isDim && (
                      <div className="demo-phase-latency">
                        {phase.latencyMs.toFixed(0)}ms
                      </div>
                    )}
                  </div>
                  {index < 4 && (
                    <div className="demo-pipeline-arrow">
                      <div
                        className={`demo-arrow-line${isCompleted ? ' completed' : ''}${isActive ? ' active' : ''}`}
                      />
                    </div>
                  )}
                </div>
              );
            })}
          </div>

          {/* Phase detail panel */}
          {activePhase && selectedPhaseIndex !== null && (
            <PhaseDetail phase={activePhase} phaseIndex={selectedPhaseIndex} />
          )}

          {/* Environment bar */}
          {demo.result && (
            <div className="demo-env-bar">
              <span className="demo-env-chip">
                <span className="demo-env-dot" />
                {demo.result.environmentReport.readinessTier}
              </span>
              <span>
                Model: {demo.result.environmentReport.defaultModel}
              </span>
              <span>
                Cloud Providers: {demo.result.environmentReport.cloudProvidersActive}
              </span>
              <span>
                Total: {demo.result.totalLatencyMs.toFixed(0)}ms
              </span>
            </div>
          )}

          <button className="demo-nav-btn" onClick={() => scrollToAct(3)}>
            Continue: Trust Tiers &#8594;
          </button>
        </div>
      </section>

      {/* ── ACT 3: TRUST TIERS ──────────────────────────────────── */}
      <section
        className="demo-act demo-tiers-section"
        ref={(el) => { actRefs.current[2] = el; }}
      >
        <div className="demo-act-inner">
          <div className="demo-section-label">Act 3</div>
          <h2 className="demo-act-title">Trust Tiers</h2>
          <p className="demo-act-subtitle">
            Six graduated levels of agent autonomy — from passive observation
            to emergency override. Click any tier to see a real-world example.
          </p>

          <div className="demo-tier-pyramid">
            {TRUST_TIERS.map((tier, index) => (
              <div
                key={tier.tier}
                className={`demo-tier-row${expandedTier === index ? ' expanded' : ''}`}
                onClick={() =>
                  setExpandedTier(expandedTier === index ? null : index)
                }
              >
                <span className="demo-tier-badge">{tier.tier}</span>
                <div className="demo-tier-info">
                  <div className="demo-tier-label">
                    {tier.label}{' '}
                    <span style={{ fontWeight: 400, color: 'var(--text-muted)' }}>
                      — {tier.description}
                    </span>
                  </div>
                  {expandedTier === index && (
                    <div className="demo-tier-example">{tier.example}</div>
                  )}
                </div>
              </div>
            ))}
          </div>

          <div className="demo-tier-callout">
            Trust tiers are enforced at the governance kernel level. Every
            action is evaluated against the agent&apos;s current tier before
            execution.
          </div>

          <button className="demo-nav-btn" onClick={() => scrollToAct(4)}>
            Continue: The Moat &#8594;
          </button>
        </div>
      </section>

      {/* ── ACT 4: THE MOAT ─────────────────────────────────────── */}
      <section
        className="demo-act demo-moat-section"
        ref={(el) => { actRefs.current[3] = el; }}
      >
        <div className="demo-act-inner">
          <div className="demo-section-label">Act 4</div>
          <h2 className="demo-act-title">The Moat</h2>
          <p className="demo-act-subtitle">
            Governance capabilities compared across platforms. The data speaks
            for itself.
          </p>

          <table className="demo-comparison-table">
            <thead>
              <tr>
                <th>Capability</th>
                <th>ArchonAI</th>
                <th>Workato</th>
                <th>Boomi</th>
                <th>Tray.ai</th>
              </tr>
            </thead>
            <tbody>
              {COMPARISON_DATA.map((row) => (
                <tr key={row.capability}>
                  <td>{row.capability}</td>
                  <td className="demo-archonai-col">
                    <span className="demo-check">&#10003;</span>{' '}
                    {row.archonai}
                  </td>
                  <td>
                    {row.workato === '\u2715' ? (
                      <span className="demo-cross">{row.workato}</span>
                    ) : (
                      row.workato
                    )}
                  </td>
                  <td>
                    {row.boomi === '\u2715' ? (
                      <span className="demo-cross">{row.boomi}</span>
                    ) : (
                      row.boomi
                    )}
                  </td>
                  <td>
                    {row.trayai === '\u2715' ? (
                      <span className="demo-cross">{row.trayai}</span>
                    ) : (
                      row.trayai
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <button className="demo-nav-btn" onClick={() => scrollToAct(5)}>
            Continue: Next Steps &#8594;
          </button>
        </div>
      </section>

      {/* ── ACT 5: CALL TO ACTION ───────────────────────────────── */}
      <section
        className="demo-act demo-cta-section"
        ref={(el) => { actRefs.current[4] = el; }}
      >
        <div className="demo-act-inner">
          <div className="demo-section-label">Act 5</div>
          <h2 className="demo-act-title">See ArchonAI in Your Environment</h2>

          <div className="demo-cta-cards">
            <div className="demo-cta-card">
              <div className="demo-cta-card-title">Technical Deep Dive</div>
              <div className="demo-cta-card-desc">
                30-minute architecture walkthrough with live API exploration
              </div>
            </div>
            <div className="demo-cta-card">
              <div className="demo-cta-card-title">Integration Assessment</div>
              <div className="demo-cta-card-desc">
                How ArchonAI&apos;s External Governance API connects to your
                agent framework
              </div>
            </div>
            <div className="demo-cta-card">
              <div className="demo-cta-card-title">Pilot Program</div>
              <div className="demo-cta-card-desc">
                Deploy in a sandbox with your real workloads
              </div>
            </div>
          </div>

          <div className="demo-contact">
            Zack Fava, Founder —{' '}
            <a href="mailto:zack@archonai.com">zack@archonai.com</a>
          </div>
        </div>
      </section>
    </div>
  );
}
