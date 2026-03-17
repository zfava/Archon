import type { OnboardingState, DeploymentResult } from '../types';

const LEVEL_LABELS: Record<string, string> = {
  approval: 'Approval Required',
  assisted: 'Assisted',
  autonomous: 'Fully Autonomous',
};

const BIZ_LABELS: Record<string, string> = {
  saas: 'SaaS',
  ecommerce: 'E-Commerce',
  healthcare: 'Healthcare',
  'financial-services': 'Financial Services',
  manufacturing: 'Manufacturing',
  'professional-services': 'Professional Services',
  other: 'Other',
};

interface Props {
  state: OnboardingState;
  deployResult: DeploymentResult | null;
  onDeploy: () => void;
}

export function ReviewDeploy({ state, deployResult, onDeploy }: Props) {
  const connectedSystems = state.systems.filter(s => s.connected);
  const enabledDepts = state.automation.departments.filter(d => d.enabled);

  if (state.deployed && deployResult) {
    return (
      <div className="ob-step-content">
        <div className="ob-deploy-success">
          <div className="ob-deploy-check">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#4ade80" strokeWidth="2">
              <circle cx="12" cy="12" r="10" />
              <path d="M8 12l3 3 5-5" />
            </svg>
          </div>
          <h2 className="ob-step-title">Deployment complete</h2>
          <p className="ob-step-desc">
            ArchonAI is configured and ready. Estimated time to full operational readiness:{' '}
            <strong>{deployResult.estimatedReadyMinutes} minutes</strong>.
          </p>

          <div className="ob-deploy-summary">
            <div className="ob-summary-section">
              <span className="ob-group-label">Agents Configured</span>
              <div className="ob-chip-list">
                {deployResult.agentsConfigured.map(a => (
                  <span key={a} className="ob-agent-chip">{a}</span>
                ))}
              </div>
            </div>
            <div className="ob-summary-section">
              <span className="ob-group-label">Strategies Applied</span>
              <div className="ob-chip-list">
                {deployResult.strategiesApplied.map(s => (
                  <span key={s} className="ob-strategy-chip">{s}</span>
                ))}
              </div>
            </div>
            <div className="ob-summary-section">
              <span className="ob-group-label">Integrations Active</span>
              <div className="ob-chip-list">
                {deployResult.integrationsActive.map(i => (
                  <span key={i} className="ob-integration-chip">{i}</span>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="ob-step-content">
      <div className="ob-step-intro">
        <h2 className="ob-step-title">Review &amp; deploy</h2>
        <p className="ob-step-desc">
          Confirm your configuration. ArchonAI will auto-configure agents, strategies,
          and integrations based on your selections.
        </p>
      </div>

      <div className="ob-review-grid">
        {/* Systems */}
        <div className="ob-review-card">
          <span className="ob-group-label">Connected Systems</span>
          <span className="ob-review-count">{connectedSystems.length}</span>
          <div className="ob-review-items">
            {connectedSystems.map(s => (
              <span key={s.id} className="ob-review-item">{s.name}</span>
            ))}
          </div>
        </div>

        {/* Business type */}
        <div className="ob-review-card">
          <span className="ob-group-label">Business Type</span>
          <span className="ob-review-value">
            {state.businessType ? BIZ_LABELS[state.businessType] : 'Not selected'}
          </span>
        </div>

        {/* Automation */}
        <div className="ob-review-card">
          <span className="ob-group-label">Automation Level</span>
          <span className="ob-review-value">{LEVEL_LABELS[state.automation.level]}</span>
          <div className="ob-review-items">
            {enabledDepts.map(d => (
              <span key={d.name} className="ob-review-item">
                {d.name}: {LEVEL_LABELS[d.level]}
              </span>
            ))}
          </div>
        </div>

        {/* Estimate */}
        <div className="ob-review-card">
          <span className="ob-group-label">Estimated Setup Time</span>
          <span className="ob-review-time">&lt; {state.estimatedMinutes} min</span>
        </div>
      </div>

      {state.deployError && (
        <div className="ob-error-banner">
          <span>{state.deployError}</span>
        </div>
      )}

      <div className="ob-deploy-actions">
        <button
          className="ob-deploy-btn"
          onClick={onDeploy}
          disabled={state.deploying}
        >
          {state.deploying ? (
            <>
              <div className="phase-spinner" />
              Deploying...
            </>
          ) : (
            'Deploy ArchonAI'
          )}
        </button>
      </div>
    </div>
  );
}
