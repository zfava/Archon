import type { PlatformPolicy, SecurityMetrics } from '../types';

interface Props {
  policies: PlatformPolicy[];
  metrics: SecurityMetrics | null;
  onTogglePolicy: (policyId: string, enabled: boolean) => void;
}

function policyTypeClass(type: string): string {
  switch (type) {
    case 'Security': return 'cp-pt--security';
    case 'Workflow': return 'cp-pt--workflow';
    case 'Agent': return 'cp-pt--agent';
    case 'RateLimit': return 'cp-pt--rate';
    case 'Compliance': return 'cp-pt--compliance';
    default: return 'cp-pt--default';
  }
}

export function GovernanceStatus({ policies, metrics, onTogglePolicy }: Props) {
  const enabledCount = policies.filter((p) => p.isEnabled).length;

  return (
    <section className="cp-card">
      <span className="cp-section-label">Governance & Policies</span>

      {/* Security metrics summary */}
      {metrics && (
        <div className="cp-gov-metrics">
          <div className="cp-gov-stat">
            <span className="cp-gov-val">{metrics.totalEvaluations.toLocaleString()}</span>
            <span className="cp-gov-lbl">Evaluations</span>
          </div>
          <div className="cp-gov-stat">
            <span className="cp-gov-val cp-gov-val--warn">
              {metrics.totalViolations.toLocaleString()}
            </span>
            <span className="cp-gov-lbl">Violations</span>
          </div>
          <div className="cp-gov-stat">
            <span className="cp-gov-val">{metrics.activePolicies}</span>
            <span className="cp-gov-lbl">Active Policies</span>
          </div>
          <div className="cp-gov-stat">
            <span className="cp-gov-val cp-gov-val--warn">
              {metrics.agentPermissionDenials.toLocaleString()}
            </span>
            <span className="cp-gov-lbl">Agent Denials</span>
          </div>
        </div>
      )}

      {/* Top violations */}
      {metrics && metrics.topViolations.length > 0 && (
        <div className="cp-violations">
          <span className="cp-sub-label">Top Violations</span>
          {metrics.topViolations.slice(0, 5).map((v) => (
            <div key={v.policyName} className="cp-violation-row">
              <span className="cp-violation-name">{v.policyName}</span>
              <span className="cp-violation-cat">{v.category}</span>
              <span className="cp-violation-count">{v.violationCount}</span>
            </div>
          ))}
        </div>
      )}

      {/* Active policies */}
      <div className="cp-policies">
        <span className="cp-sub-label">
          Platform Policies ({enabledCount}/{policies.length} enabled)
        </span>
        {policies.length === 0 ? (
          <div className="cp-policies-empty">No policies configured.</div>
        ) : (
          <div className="cp-policy-list">
            {policies.map((policy) => (
              <div
                key={policy.id}
                className={`cp-policy-row ${!policy.isEnabled ? 'cp-policy-row--disabled' : ''}`}
              >
                <div className="cp-policy-info">
                  <div className="cp-policy-top">
                    <span className="cp-policy-name">{policy.name}</span>
                    <span className={`cp-policy-type ${policyTypeClass(policy.policyType)}`}>
                      {policy.policyType}
                    </span>
                  </div>
                  <span className="cp-policy-desc">{policy.description}</span>
                  <span className="cp-policy-target">Target: {policy.targetResource}</span>
                </div>
                <label className="cp-toggle">
                  <input
                    type="checkbox"
                    checked={policy.isEnabled}
                    onChange={(e) => onTogglePolicy(policy.id, e.target.checked)}
                  />
                  <span className="cp-toggle-slider" />
                </label>
              </div>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
