import type { OnboardingState, DeploymentResult } from '../types';
import { ShadowScenarios } from './ShadowScenarios';
import { ComplianceBanner } from './ComplianceBanner';
import { TEMPLATES } from '../templates';

const LEVEL_LABELS: Record<string, string> = {
  approval: 'Approval Required',
  assisted: 'Assisted',
  autonomous: 'Fully Autonomous',
};

const BIZ_LABELS: Record<string, string> = {
  healthcare: 'Healthcare',
  'financial-services': 'Financial Services',
  manufacturing: 'Manufacturing',
  'professional-services': 'Professional Services',
  energy: 'Energy & Utilities',
  defense: 'Defense & Government',
};

interface Props {
  state: OnboardingState;
  deployResult: DeploymentResult | null;
  onDeploy: () => void;
}

export function ReviewDeploy({ state, deployResult, onDeploy }: Props) {
  const connectedSystems = state.systems.filter(s => s.connected);
  const enabledDepts = state.automation.departments.filter(d => d.enabled);
  const tpl = state.selectedTemplate;

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
          <h2 className="ob-step-title">
            {tpl ? `${tpl.name} deployed` : 'Deployment complete'}
          </h2>
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
            {deployResult.workflowsCreated && deployResult.workflowsCreated.length > 0 && (
              <div className="ob-summary-section">
                <span className="ob-group-label">Workflows Created</span>
                <div className="ob-chip-list">
                  {deployResult.workflowsCreated.map(w => (
                    <span key={w} className="ob-workflow-chip">{w}</span>
                  ))}
                </div>
              </div>
            )}
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

      <ComplianceBanner businessType={state.businessType} />

      <div className="ob-review-grid">
        {/* Template */}
        {tpl && (
          <div className="ob-review-card">
            <span className="ob-group-label">Template</span>
            <span className="ob-review-value">{tpl.name}</span>
            <span className="ob-review-sub">{tpl.industry}</span>
          </div>
        )}

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

      {/* Compliance notes */}
      {(() => {
        const complianceNotes = tpl?.complianceNotes
          ?? (state.businessType === 'manufacturing'
            ? TEMPLATES.find(t => t.id === 'manufacturing')?.complianceNotes
            : state.businessType === 'healthcare'
            ? TEMPLATES.find(t => t.id === 'healthcare')?.complianceNotes
            : state.businessType === 'financial-services'
            ? TEMPLATES.find(t => t.id === 'financial-services')?.complianceNotes
            : state.businessType === 'energy'
            ? TEMPLATES.find(t => t.id === 'energy')?.complianceNotes
            : state.businessType === 'defense'
            ? TEMPLATES.find(t => t.id === 'defense')?.complianceNotes
            : state.businessType === 'professional-services'
            ? TEMPLATES.find(t => t.id === 'professional-services')?.complianceNotes
            : undefined);
        if (!complianceNotes || complianceNotes.length === 0) return null;
        return (
          <div className="ob-compliance-section">
            <span className="ob-group-label">Compliance &amp; Audit Readiness</span>
            <div className="ob-compliance-list">
              {complianceNotes.map(c => (
                <div key={c.standard} className="ob-compliance-row">
                  <span className="ob-compliance-standard">{c.standard}</span>
                  <span className={`ob-compliance-badge ob-compliance-badge--${c.status}`}>
                    {c.status}
                  </span>
                  <span className="ob-compliance-desc">{c.description}</span>
                </div>
              ))}
            </div>
          </div>
        );
      })()}

      {/* Shadow scenarios */}
      {(() => {
        const scenarios = tpl?.shadowScenarios
          ?? (state.businessType === 'manufacturing'
            ? TEMPLATES.find(t => t.id === 'manufacturing')?.shadowScenarios
            : state.businessType === 'healthcare'
            ? TEMPLATES.find(t => t.id === 'healthcare')?.shadowScenarios
            : state.businessType === 'financial-services'
            ? TEMPLATES.find(t => t.id === 'financial-services')?.shadowScenarios
            : state.businessType === 'energy'
            ? TEMPLATES.find(t => t.id === 'energy')?.shadowScenarios
            : state.businessType === 'defense'
            ? TEMPLATES.find(t => t.id === 'defense')?.shadowScenarios
            : state.businessType === 'professional-services'
            ? TEMPLATES.find(t => t.id === 'professional-services')?.shadowScenarios
            : undefined);
        if (!scenarios || scenarios.length === 0) return null;
        return <ShadowScenarios scenarios={scenarios} />;
      })()}

      {/* Trust tier defaults */}
      {(() => {
        const trustTierDefaults = tpl?.trustTierDefaults
          ?? (state.businessType === 'manufacturing'
            ? TEMPLATES.find(t => t.id === 'manufacturing')?.trustTierDefaults
            : state.businessType === 'healthcare'
            ? TEMPLATES.find(t => t.id === 'healthcare')?.trustTierDefaults
            : state.businessType === 'financial-services'
            ? TEMPLATES.find(t => t.id === 'financial-services')?.trustTierDefaults
            : state.businessType === 'energy'
            ? TEMPLATES.find(t => t.id === 'energy')?.trustTierDefaults
            : state.businessType === 'defense'
            ? TEMPLATES.find(t => t.id === 'defense')?.trustTierDefaults
            : state.businessType === 'professional-services'
            ? TEMPLATES.find(t => t.id === 'professional-services')?.trustTierDefaults
            : undefined);
        if (!trustTierDefaults || trustTierDefaults.length === 0) return null;
        return (
        <div className="ob-trust-section">
          <span className="ob-group-label">Default Trust Tier Policies</span>
          <div className="ob-trust-table">
            <div className="ob-trust-header-row">
              <span className="ob-trust-col-scope">Action Scope</span>
              <span className="ob-trust-col-tier">Max Tier</span>
              <span className="ob-trust-col-rationale">Rationale</span>
            </div>
            {trustTierDefaults.map(t => (
              <div key={t.actionScope} className="ob-trust-row">
                <span className="ob-trust-col-scope">{t.actionScope}</span>
                <span className="ob-trust-col-tier ob-trust-tier-badge">{t.maxTier}</span>
                <span className="ob-trust-col-rationale">{t.rationale}</span>
              </div>
            ))}
          </div>
        </div>
        );
      })()}

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
